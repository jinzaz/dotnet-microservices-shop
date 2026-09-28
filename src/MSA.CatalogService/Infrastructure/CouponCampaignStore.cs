using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Infrastructure;

internal sealed class CouponCampaignStore(CatalogDbContext db) : ICouponCampaignRepository, ICouponCampaignReadService
{
    async Task<CouponCampaign?> ICouponCampaignRepository.FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!db.HasActiveTransaction)
            throw new InvalidOperationException("Coupon campaign writes require an active transaction.");
        return await db.CouponCampaigns
            .Where(campaign => campaign.Id == id)
            .ForUpdate()
            .SingleOrDefaultAsync(cancellationToken);
    }

    async Task<CouponClaimCounts> ICouponCampaignRepository.CountClaimsAsync(
        Guid campaignId,
        string userId,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var claims = db.CouponClaims.Where(claim => claim.CampaignId == campaignId);
        var userTotal = await claims.CountAsync(claim => claim.UserId == userId, cancellationToken);
        var userToday = await claims.CountAsync(claim => claim.UserId == userId && claim.ClaimDay == day, cancellationToken);
        var campaignToday = await claims.CountAsync(claim => claim.ClaimDay == day, cancellationToken);
        return new CouponClaimCounts(userTotal, userToday, campaignToday);
    }

    void ICouponCampaignRepository.Add(CouponCampaign campaign) => db.CouponCampaigns.Add(campaign);

    void ICouponCampaignRepository.AddClaim(CouponClaim claim) => db.CouponClaims.Add(claim);

    async Task<PagedResult<CouponCampaignDto>> ICouponCampaignReadService.ListAsync(
        int skip,
        int take,
        string? status,
        Guid? promotionId,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = db.CouponCampaigns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = Enum.TryParse<CouponCampaignStatus>(status.Trim(), ignoreCase: true, out var value)
                ? value
                : throw new ArgumentException($"Unknown CouponCampaignStatus value '{status}'.", nameof(status));
            query = query.Where(campaign => campaign.Status == parsed);
        }
        if (promotionId.HasValue)
            query = query.Where(campaign => campaign.PromotionId == promotionId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(campaign => campaign.UpdatedOnUtc)
            .ThenBy(campaign => campaign.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<CouponCampaignDto>(items.Select(ToDto).ToArray(), skip, take, total);
    }

    async Task<CouponCampaignDto?> ICouponCampaignReadService.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await db.CouponCampaigns.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return campaign is null ? null : ToDto(campaign);
    }

    async Task<CouponCampaignStatsDto> ICouponCampaignReadService.GetStatsAsync(
        Guid id,
        DateOnly today,
        int days,
        CancellationToken cancellationToken)
    {
        var claimed = await db.CouponCampaigns.AsNoTracking()
            .Where(campaign => campaign.Id == id)
            .Select(campaign => (int?)campaign.ClaimedQuantity)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Coupon campaign was not found.");
        var claims = db.CouponClaims.AsNoTracking().Where(claim => claim.CampaignId == id);
        var firstDay = today.AddDays(1 - days);
        var perDay = await claims
            .Where(claim => claim.ClaimDay >= firstDay)
            .GroupBy(claim => claim.ClaimDay)
            .Select(group => new { Day = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Day, item => item.Count, cancellationToken);
        var users = await claims.Select(claim => claim.UserId).Distinct().CountAsync(cancellationToken);
        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new CouponClaimDayDto(day, perDay.GetValueOrDefault(day)))
            .ToArray();
        return new CouponCampaignStatsDto(claimed, perDay.GetValueOrDefault(today), users, daily);
    }

    async Task<PagedResult<CouponClaimDto>> ICouponCampaignReadService.ListClaimsAsync(
        Guid id,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = db.CouponClaims.AsNoTracking().Where(claim => claim.CampaignId == id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(claim => claim.ClaimedOnUtc)
            .ThenBy(claim => claim.Id)
            .Skip(skip)
            .Take(take)
            .Select(claim => new CouponClaimDto(claim.Id, claim.UserId, claim.CouponId, claim.ClaimedOnUtc))
            .ToArrayAsync(cancellationToken);
        return new PagedResult<CouponClaimDto>(items, skip, take, total);
    }

    async Task<IReadOnlyList<StorefrontCouponCampaignDto>> ICouponCampaignReadService.ListStorefrontAsync(
        Guid? campaignId,
        string? userId,
        DateTimeOffset now,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // Paused and draft campaigns are hidden; ended ones only show when opened by link so the page can explain why.
        var query = db.CouponCampaigns.AsNoTracking();
        query = campaignId.HasValue
            ? query.Where(campaign => campaign.Id == campaignId.Value
                && (campaign.Status == CouponCampaignStatus.Active || campaign.Status == CouponCampaignStatus.Ended))
            : query.Where(campaign => campaign.Status == CouponCampaignStatus.Active
                && campaign.IsListed
                && campaign.ClaimEndsOnUtc > now);
        var campaigns = await query.Take(200).ToArrayAsync(cancellationToken);
        if (campaigns.Length == 0)
            return [];

        var promotionIds = campaigns.Select(campaign => campaign.PromotionId).Distinct().ToArray();
        var promotions = await db.Promotions.AsNoTracking()
            .Include(promotion => promotion.Rules)
            .Include(promotion => promotion.Benefits)
            .Include(promotion => promotion.Targets)
            .AsSplitQuery()
            .Where(promotion => promotionIds.Contains(promotion.Id) && promotion.Status == PromotionStatus.Active)
            .ToDictionaryAsync(promotion => promotion.Id, cancellationToken);

        var ids = campaigns.Select(campaign => campaign.Id).ToArray();
        var todayCounts = await db.CouponClaims.AsNoTracking()
            .Where(claim => ids.Contains(claim.CampaignId) && claim.ClaimDay == today)
            .GroupBy(claim => claim.CampaignId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);
        var mine = string.IsNullOrWhiteSpace(userId)
            ? new Dictionary<Guid, (int Total, int Today)>()
            : await db.CouponClaims.AsNoTracking()
                .Where(claim => ids.Contains(claim.CampaignId) && claim.UserId == userId)
                .GroupBy(claim => claim.CampaignId)
                .Select(group => new { group.Key, Total = group.Count(), Today = group.Count(claim => claim.ClaimDay == today) })
                .ToDictionaryAsync(item => item.Key, item => (item.Total, item.Today), cancellationToken);

        return campaigns
            .Where(campaign => promotions.ContainsKey(campaign.PromotionId))
            .Select(campaign =>
            {
                var promotion = promotions[campaign.PromotionId];
                var claimedToday = todayCounts.GetValueOrDefault(campaign.Id);
                var (myTotal, myToday) = mine.GetValueOrDefault(campaign.Id);
                return new StorefrontCouponCampaignDto(
                    campaign.Id,
                    campaign.Title,
                    campaign.Description,
                    promotion.Id,
                    promotion.Name,
                    promotion.Rules.OrderBy(rule => rule.Sequence).Select(rule => new StorefrontPromotionRuleDto(rule.RuleType, rule.SchemaVersion, rule.ConfigurationJson)).ToArray(),
                    promotion.Benefits.OrderBy(benefit => benefit.Sequence).Select(benefit => new StorefrontPromotionBenefitDto(benefit.BenefitType, benefit.SchemaVersion, benefit.ConfigurationJson)).ToArray(),
                    promotion.Targets.OrderBy(target => target.Sequence).Select(target => new StorefrontPromotionTargetDto(target.TargetType, target.TargetKey, target.IsExcluded)).ToArray(),
                    campaign.ClaimStartsOnUtc,
                    campaign.ClaimEndsOnUtc,
                    campaign.TotalQuantity,
                    campaign.ClaimedQuantity,
                    campaign.DailyQuantity,
                    claimedToday,
                    campaign.PerUserLimit,
                    campaign.PerUserDailyLimit,
                    myTotal,
                    myToday,
                    campaign.ValidityMode.ToString(),
                    campaign.ValidFromUtc,
                    campaign.ValidUntilUtc,
                    campaign.ValidDays,
                    campaign.UsesPerCoupon,
                    StateOf(campaign, promotion, claimedToday, myTotal, myToday, now));
            })
            .OrderBy(item => StateRank(item.ClaimState))
            .ThenBy(item => item.ClaimStartsOnUtc)
            .ThenBy(item => item.ClaimEndsOnUtc)
            .ToArray();
    }

    /// <summary>Mirrors the checks in <see cref="CouponCampaign.Claim"/> so the button state matches what a claim would do.</summary>
    internal static string StateOf(
        CouponCampaign campaign,
        Promotion promotion,
        int claimedToday,
        int myTotal,
        int myToday,
        DateTimeOffset now)
    {
        if (campaign.Status != CouponCampaignStatus.Active || now >= campaign.ClaimEndsOnUtc || now >= promotion.EndsOnUtc)
            return CouponClaimStates.Ended;
        if (now < campaign.ClaimStartsOnUtc)
            return CouponClaimStates.NotStarted;
        if (campaign.TotalQuantity.HasValue && campaign.ClaimedQuantity >= campaign.TotalQuantity.Value)
            return CouponClaimStates.SoldOut;
        if (myTotal >= campaign.PerUserLimit)
            return CouponClaimStates.LimitReached;
        if (campaign.PerUserDailyLimit.HasValue && myToday >= campaign.PerUserDailyLimit.Value)
            return CouponClaimStates.DailyLimitReached;
        if (campaign.DailyQuantity.HasValue && claimedToday >= campaign.DailyQuantity.Value)
            return CouponClaimStates.DailySoldOut;
        return CouponClaimStates.Claimable;
    }

    private static int StateRank(string state) => state switch
    {
        CouponClaimStates.Claimable => 0,
        CouponClaimStates.NotStarted => 1,
        CouponClaimStates.DailySoldOut or CouponClaimStates.DailyLimitReached => 2,
        CouponClaimStates.LimitReached => 3,
        _ => 4
    };

    private static CouponCampaignDto ToDto(CouponCampaign campaign) => new(
        campaign.Id,
        campaign.PromotionId,
        campaign.Title,
        campaign.Description,
        campaign.Status.ToString(),
        campaign.ClaimStartsOnUtc,
        campaign.ClaimEndsOnUtc,
        campaign.TotalQuantity,
        campaign.ClaimedQuantity,
        campaign.DailyQuantity,
        campaign.PerUserLimit,
        campaign.PerUserDailyLimit,
        campaign.ValidityMode.ToString(),
        campaign.ValidFromUtc,
        campaign.ValidUntilUtc,
        campaign.ValidDays,
        campaign.UsesPerCoupon,
        campaign.IsListed,
        campaign.CreatedOnUtc,
        campaign.UpdatedOnUtc,
        campaign.CreatedBy,
        campaign.UpdatedBy,
        campaign.Version);
}
