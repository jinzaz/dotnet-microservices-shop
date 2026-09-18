using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using System.Text.Json;

namespace MSA.OrderService.Application.Coupons;

public static class WalletCouponStatus
{
    public const string Available = "Available";
    public const string NotStarted = "NotStarted";
    public const string Used = "Used";
    public const string Expired = "Expired";
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// A coupon in the buyer's wallet: issued to them, or a public code they have redeemed before.
/// Only the masked code is known here; wallet coupons are applied by <see cref="CouponId"/>.
/// </summary>
public sealed record WalletCouponDto(
    Guid CouponId,
    Guid PromotionId,
    string PromotionName,
    string MaskedCode,
    bool IsAssigned,
    string Status,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    int UsedCount,
    int PerUserLimit,
    DateTimeOffset? LastUsedOnUtc,
    IReadOnlyList<PromotionRuleContract> Rules,
    IReadOnlyList<PromotionBenefitContract> Benefits,
    IReadOnlyList<PromotionTargetContract> Targets);

public sealed record GetMyCouponsQuery(string UserId) : IQuery<IReadOnlyList<WalletCouponDto>>;

public sealed record CouponUsageDto(
    Guid CouponId,
    int ReservedCount,
    int ConsumedCount,
    int ReleasedCount,
    decimal ConsumedDiscountTotal,
    int? RemainingQuota,
    DateTimeOffset? LastRedeemedOnUtc);

/// <summary>Redemption statistics the catalog cannot know: reservations and consumption live in Ordering.</summary>
public sealed record GetCouponUsageQuery(IReadOnlyList<Guid> CouponIds) : IQuery<IReadOnlyList<CouponUsageDto>>;

internal sealed class GetMyCouponsQueryHandler(OrderDbContext db, TimeProvider clock)
    : IRequestHandler<GetMyCouponsQuery, IReadOnlyList<WalletCouponDto>>
{
    public async Task<IReadOnlyList<WalletCouponDto>> Handle(GetMyCouponsQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var usage = await db.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.UserId == request.UserId && redemption.Status != CouponRedemptionStatus.Released)
            .GroupBy(redemption => redemption.CouponId)
            .Select(group => new { CouponId = group.Key, Count = group.Count(), LastUsed = group.Max(item => item.CreatedOnUtc) })
            .ToDictionaryAsync(item => item.CouponId, cancellationToken);
        var redeemedIds = usage.Keys.ToArray();
        var coupons = await db.CouponProjections
            .AsNoTracking()
            .Where(coupon => coupon.AssignedUserId == request.UserId || redeemedIds.Contains(coupon.Id))
            .ToArrayAsync(cancellationToken);
        var promotionIds = coupons.Select(coupon => coupon.PromotionId).Distinct().ToArray();
        var promotions = await db.PromotionProjections
            .AsNoTracking()
            .Where(promotion => promotionIds.Contains(promotion.Id))
            .ToDictionaryAsync(promotion => promotion.Id, cancellationToken);

        return coupons
            .Select(coupon =>
            {
                promotions.TryGetValue(coupon.PromotionId, out var promotion);
                usage.TryGetValue(coupon.Id, out var used);
                var usedCount = used?.Count ?? 0;
                return new WalletCouponDto(
                    coupon.Id,
                    coupon.PromotionId,
                    promotion?.Name ?? coupon.MaskedCode,
                    coupon.MaskedCode,
                    coupon.AssignedUserId == request.UserId,
                    StatusOf(coupon, promotion, usedCount, now),
                    coupon.ValidFromUtc,
                    coupon.ValidUntilUtc,
                    usedCount,
                    coupon.PerUserLimit,
                    used?.LastUsed,
                    Parse<PromotionRuleContract>(promotion?.RulesJson),
                    Parse<PromotionBenefitContract>(promotion?.BenefitsJson),
                    Parse<PromotionTargetContract>(promotion?.TargetsJson));
            })
            .OrderBy(coupon => coupon.Status == WalletCouponStatus.Available ? 0 : coupon.Status == WalletCouponStatus.NotStarted ? 1 : 2)
            .ThenBy(coupon => coupon.ValidUntilUtc)
            .ToArray();
    }

    internal static string StatusOf(CouponProjection coupon, PromotionProjection? promotion, int usedCount, DateTimeOffset now)
    {
        if (usedCount >= coupon.PerUserLimit)
            return WalletCouponStatus.Used;
        if (now >= coupon.ValidUntilUtc || coupon.Status == "Expired" || promotion is { } ended && now >= ended.EndsOnUtc)
            return WalletCouponStatus.Expired;
        if (coupon.Status != "Active" || promotion is null || promotion.Status != "Active")
            return WalletCouponStatus.Unavailable;
        return now < coupon.ValidFromUtc || now < promotion.StartsOnUtc
            ? WalletCouponStatus.NotStarted
            : WalletCouponStatus.Available;
    }

    private static IReadOnlyList<T> Parse<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<IReadOnlyList<T>>(json) ?? [];
}

internal sealed class GetCouponUsageQueryHandler(OrderDbContext db)
    : IRequestHandler<GetCouponUsageQuery, IReadOnlyList<CouponUsageDto>>
{
    public async Task<IReadOnlyList<CouponUsageDto>> Handle(GetCouponUsageQuery request, CancellationToken cancellationToken)
    {
        var ids = request.CouponIds.Distinct().ToArray();
        var redemptions = await db.CouponRedemptions
            .AsNoTracking()
            .Where(redemption => ids.Contains(redemption.CouponId))
            .GroupBy(redemption => new { redemption.CouponId, redemption.Status })
            .Select(group => new
            {
                group.Key.CouponId,
                group.Key.Status,
                Count = group.Count(),
                Discount = group.Sum(item => item.DiscountAmount),
                Last = group.Max(item => item.CreatedOnUtc)
            })
            .ToArrayAsync(cancellationToken);
        var quotas = await db.CouponQuotaBuckets
            .AsNoTracking()
            .Where(bucket => ids.Contains(bucket.CouponId))
            .GroupBy(bucket => bucket.CouponId)
            .Select(group => new { CouponId = group.Key, Remaining = group.Sum(bucket => bucket.Capacity - bucket.ReservedCount - bucket.ConsumedCount) })
            .ToDictionaryAsync(item => item.CouponId, item => item.Remaining, cancellationToken);

        return ids.Select(id =>
        {
            var rows = redemptions.Where(row => row.CouponId == id).ToArray();
            int CountOf(CouponRedemptionStatus status) => rows.Where(row => row.Status == status).Sum(row => row.Count);
            return new CouponUsageDto(
                id,
                CountOf(CouponRedemptionStatus.Reserved),
                CountOf(CouponRedemptionStatus.Consumed),
                CountOf(CouponRedemptionStatus.Released),
                rows.Where(row => row.Status == CouponRedemptionStatus.Consumed).Sum(row => row.Discount),
                quotas.TryGetValue(id, out var remaining) ? remaining : null,
                rows.Length == 0 ? null : rows.Max(row => row.Last));
        }).ToArray();
    }
}
