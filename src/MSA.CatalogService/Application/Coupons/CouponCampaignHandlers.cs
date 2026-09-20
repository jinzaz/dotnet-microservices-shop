using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Application;

/// <summary>Daily claim limits reset on the store's business calendar, not at UTC midnight.</summary>
public static class CouponClaimCalendar
{
    // ponytail: one fixed business time zone; make it configurable if the store ever sells across regions.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

internal static class CouponCampaignRules
{
    public static CouponCampaignDefinition ToDefinition(CouponCampaignInput input) => new(
        input.Title,
        input.Description,
        input.ClaimStartsOnUtc,
        input.ClaimEndsOnUtc,
        input.TotalQuantity,
        input.DailyQuantity,
        input.PerUserLimit,
        input.PerUserDailyLimit,
        PromotionDefinitionMapper.ParseEnum<CouponValidityMode>(input.ValidityMode, nameof(input.ValidityMode)),
        input.ValidFromUtc,
        input.ValidUntilUtc,
        input.ValidDays,
        input.UsesPerCoupon,
        input.IsListed);

    /// <summary>Claimed coupons must fit inside the promotion, otherwise Ordering would reject them at checkout.</summary>
    public static void EnsureFits(Promotion promotion, CouponCampaignDefinition definition)
    {
        if (promotion.TriggerType != PromotionTriggerType.Coupon)
            throw new InvalidOperationException("Claim campaigns can only reference a Coupon-triggered promotion.");
        if (promotion.Status is PromotionStatus.Ended or PromotionStatus.Archived)
            throw new InvalidOperationException("The promotion has ended; create a new promotion for this campaign.");
        if (definition.ClaimEndsOnUtc > promotion.EndsOnUtc)
            throw new InvalidOperationException("Claiming must end no later than the promotion.");
        if (definition.ValidityMode == CouponValidityMode.Fixed
            && (definition.ValidFromUtc < promotion.StartsOnUtc || definition.ValidUntilUtc > promotion.EndsOnUtc))
            throw new InvalidOperationException("Coupon validity must be contained by the promotion window.");
    }
}

internal sealed class CreateCouponCampaignHandler(
    IPromotionRepository promotions,
    ICouponCampaignRepository campaigns,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateCouponCampaignCommand, Guid>
{
    public async Task<Guid> Handle(CreateCouponCampaignCommand request, CancellationToken cancellationToken)
    {
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        var definition = CouponCampaignRules.ToDefinition(request.Definition);
        CouponCampaignRules.EnsureFits(promotion, definition);

        var now = clock.GetUtcNow();
        var campaign = new CouponCampaign(Guid.NewGuid(), promotion.Id, definition, request.Actor, now);
        campaigns.Add(campaign);
        audit.Record("CouponCampaign", campaign.Id, "DraftCreated", request.Actor, now, new
        {
            campaign.PromotionId,
            campaign.Title,
            campaign.TotalQuantity,
            campaign.PerUserLimit
        });
        return campaign.Id;
    }
}

internal sealed class ReviseCouponCampaignHandler(
    IPromotionRepository promotions,
    ICouponCampaignRepository campaigns,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ReviseCouponCampaignCommand, bool>
{
    public async Task<bool> Handle(ReviseCouponCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetRequiredAsync(request.CampaignId, cancellationToken);
        campaign.EnsureVersion(request.ExpectedVersion);
        var promotion = await promotions.GetRequiredAsync(campaign.PromotionId, cancellationToken);
        var definition = CouponCampaignRules.ToDefinition(request.Definition);
        CouponCampaignRules.EnsureFits(promotion, definition);

        var now = clock.GetUtcNow();
        campaign.Revise(definition, request.Actor, now);
        audit.Record("CouponCampaign", campaign.Id, "DraftRevised", request.Actor, now);
        return true;
    }
}

internal sealed class ChangeCouponCampaignStatusHandler(
    IPromotionRepository promotions,
    ICouponCampaignRepository campaigns,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ChangeCouponCampaignStatusCommand, bool>
{
    public async Task<bool> Handle(ChangeCouponCampaignStatusCommand request, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetRequiredAsync(request.CampaignId, cancellationToken);
        campaign.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        if (request.Action is CouponCampaignAction.Publish or CouponCampaignAction.Resume)
        {
            var promotion = await promotions.GetRequiredAsync(campaign.PromotionId, cancellationToken);
            if (promotion.Status != PromotionStatus.Active)
                throw new InvalidOperationException("Publish the coupon promotion before opening its claim campaign.");
        }

        switch (request.Action)
        {
            case CouponCampaignAction.Publish:
                campaign.Publish(request.Actor, now);
                break;
            case CouponCampaignAction.Pause:
                campaign.Pause(request.Actor, now);
                break;
            case CouponCampaignAction.Resume:
                campaign.Resume(request.Actor, now);
                break;
            case CouponCampaignAction.End:
                campaign.End(request.Actor, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unsupported campaign action.");
        }

        audit.Record("CouponCampaign", campaign.Id, request.Action.ToString(), request.Actor, now);
        return true;
    }
}

internal sealed class ChangeCouponCampaignQuantityHandler(
    ICouponCampaignRepository campaigns,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ChangeCouponCampaignQuantityCommand, bool>
{
    public async Task<bool> Handle(ChangeCouponCampaignQuantityCommand request, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetRequiredAsync(request.CampaignId, cancellationToken);
        campaign.EnsureVersion(request.ExpectedVersion);
        var previous = campaign.TotalQuantity;
        var now = clock.GetUtcNow();
        campaign.ChangeTotalQuantity(request.TotalQuantity, request.Actor, now);
        audit.Record("CouponCampaign", campaign.Id, "QuantityChanged", request.Actor, now, new
        {
            Previous = previous,
            Current = campaign.TotalQuantity,
            campaign.ClaimedQuantity
        });
        return true;
    }
}

/// <summary>
/// Claims run under the campaign row lock: counters, per-user and daily limits are checked and the personal coupon
/// is issued in one transaction, so a sold-out campaign can never over-issue.
/// </summary>
internal sealed class ClaimCouponHandler(
    ICouponCampaignRepository campaigns,
    IPromotionRepository promotions,
    ICouponRepository coupons,
    ICatalogSnapshotPublisher snapshots,
    TimeProvider clock) : IRequestHandler<ClaimCouponCommand, CouponClaimResultDto>
{
    public async Task<CouponClaimResultDto> Handle(ClaimCouponCommand request, CancellationToken cancellationToken)
    {
        // ponytail: one locked row per campaign serializes claims; split the counter into buckets if a single drop
        // needs more than a few hundred claims per second.
        var campaign = await campaigns.FindForUpdateAsync(request.CampaignId, cancellationToken)
            ?? throw new KeyNotFoundException("Coupon campaign was not found.");
        var promotion = await promotions.GetRequiredAsync(campaign.PromotionId, cancellationToken);
        var now = clock.GetUtcNow();
        if (promotion.Status != PromotionStatus.Active)
            throw new CouponClaimRejectedException(CouponClaimRejection.Unavailable, "The coupon promotion is not active.");

        var today = CouponClaimCalendar.DayOf(now);
        var counts = await campaigns.CountClaimsAsync(campaign.Id, request.UserId, today, cancellationToken);
        var (validFrom, validUntil) = campaign.Claim(counts, promotion.StartsOnUtc, promotion.EndsOnUtc, now);

        var code = await GenerateUniqueCodeAsync(cancellationToken);
        var coupon = new Coupon(
            Guid.NewGuid(),
            promotion.Id,
            code,
            validFrom,
            validUntil,
            request.UserId,
            totalLimit: null,
            campaign.UsesPerCoupon,
            CouponQuotaStrategy.SingleRow,
            bucketCount: null,
            request.UserId,
            now,
            campaign.Id);
        coupon.Activate(request.UserId, now);
        coupons.Add(coupon);
        snapshots.Publish(coupon);
        campaigns.AddClaim(new CouponClaim(Guid.NewGuid(), campaign.Id, request.UserId, coupon.Id, now, today));

        return new CouponClaimResultDto(
            coupon.Id,
            coupon.MaskedCode,
            coupon.ValidFromUtc,
            coupon.ValidUntilUtc,
            campaign.RemainingQuantity,
            counts.UserTotal + 1,
            campaign.PerUserLimit);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        // The plaintext is never shown: claimed coupons are applied from the wallet by id.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = Coupon.GenerateCode("CLM");
            if (!await coupons.CodeDigestExistsAsync(Coupon.ComputeCodeDigest(code), null, cancellationToken))
                return code;
        }
        throw new InvalidOperationException("Could not generate a unique coupon code; try again.");
    }
}

internal sealed class ListCouponCampaignsHandler(ICouponCampaignReadService reads)
    : IRequestHandler<ListCouponCampaignsQuery, PagedResult<CouponCampaignDto>>
{
    public Task<PagedResult<CouponCampaignDto>> Handle(ListCouponCampaignsQuery request, CancellationToken cancellationToken) =>
        reads.ListAsync(request.Skip, request.Take, request.Status, request.PromotionId, cancellationToken);
}

internal sealed class GetCouponCampaignHandler(ICouponCampaignReadService reads)
    : IRequestHandler<GetCouponCampaignQuery, CouponCampaignDto?>
{
    public Task<CouponCampaignDto?> Handle(GetCouponCampaignQuery request, CancellationToken cancellationToken) =>
        reads.GetAsync(request.CampaignId, cancellationToken);
}

internal sealed class GetCouponCampaignStatsHandler(ICouponCampaignReadService reads, TimeProvider clock)
    : IRequestHandler<GetCouponCampaignStatsQuery, CouponCampaignStatsDto>
{
    public Task<CouponCampaignStatsDto> Handle(GetCouponCampaignStatsQuery request, CancellationToken cancellationToken) =>
        reads.GetStatsAsync(
            request.CampaignId,
            CouponClaimCalendar.DayOf(clock.GetUtcNow()),
            Math.Clamp(request.Days, 1, 90),
            cancellationToken);
}

internal sealed class ListCouponClaimsHandler(ICouponCampaignReadService reads)
    : IRequestHandler<ListCouponClaimsQuery, PagedResult<CouponClaimDto>>
{
    public Task<PagedResult<CouponClaimDto>> Handle(ListCouponClaimsQuery request, CancellationToken cancellationToken) =>
        reads.ListClaimsAsync(request.CampaignId, request.Skip, request.Take, cancellationToken);
}

internal sealed class ListClaimableCouponCampaignsHandler(ICouponCampaignReadService reads, TimeProvider clock)
    : IRequestHandler<ListClaimableCouponCampaignsQuery, IReadOnlyList<StorefrontCouponCampaignDto>>
{
    public Task<IReadOnlyList<StorefrontCouponCampaignDto>> Handle(
        ListClaimableCouponCampaignsQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return reads.ListStorefrontAsync(null, request.UserId, now, CouponClaimCalendar.DayOf(now), cancellationToken);
    }
}

internal sealed class GetStorefrontCouponCampaignHandler(ICouponCampaignReadService reads, TimeProvider clock)
    : IRequestHandler<GetStorefrontCouponCampaignQuery, StorefrontCouponCampaignDto?>
{
    public async Task<StorefrontCouponCampaignDto?> Handle(
        GetStorefrontCouponCampaignQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var matches = await reads.ListStorefrontAsync(
            request.CampaignId,
            request.UserId,
            now,
            CouponClaimCalendar.DayOf(now),
            cancellationToken);
        return matches.SingleOrDefault();
    }
}
