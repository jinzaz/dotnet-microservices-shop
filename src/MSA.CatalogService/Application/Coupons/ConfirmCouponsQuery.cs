using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

/// <param name="Reason">不可用的原因；可用时为 null。</param>
public sealed record ConfirmedCouponDto(
    Guid CouponId,
    bool Usable,
    string? Reason,
    string Status,
    Guid PromotionId);

public sealed record ConfirmedCoupons(IReadOnlyList<ConfirmedCouponDto> Items);

/// <summary>
/// 供下游（订单）在成交前确认券此刻是否还能用。券投影是最终一致的，作废、改期、改归属都可能还没同步过去。
/// 这里只回答目录自己知道的那部分：状态、有效期、归属人，以及所属促销是否在活动中。
/// 配额余量不在这里 —— 券的消耗计数落在订单库，仍由订单本地把关。
/// </summary>
public sealed record ConfirmCouponsQuery(IReadOnlyList<Guid> CouponIds, string? UserId)
    : IQuery<ConfirmedCoupons>;

internal sealed class ConfirmCouponsQueryHandler(CatalogDbContext db, TimeProvider clock)
    : IRequestHandler<ConfirmCouponsQuery, ConfirmedCoupons>
{
    public const int MaximumCoupons = 50;

    public async Task<ConfirmedCoupons> Handle(ConfirmCouponsQuery request, CancellationToken cancellationToken)
    {
        var requested = (request.CouponIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
            return new ConfirmedCoupons([]);
        if (requested.Length > MaximumCoupons)
            throw new ArgumentException($"At most {MaximumCoupons} coupons can be confirmed in one call.", nameof(request));

        var now = clock.GetUtcNow();
        var rows = await db.Coupons
            .AsNoTracking()
            .Where(coupon => requested.Contains(coupon.Id))
            .Join(
                db.Promotions.AsNoTracking(),
                coupon => coupon.PromotionId,
                promotion => promotion.Id,
                (coupon, promotion) => new
                {
                    coupon.Id,
                    coupon.PromotionId,
                    coupon.Status,
                    coupon.ValidFromUtc,
                    coupon.ValidUntilUtc,
                    coupon.AssignedUserId,
                    PromotionStatus = promotion.Status,
                    PromotionStartsOnUtc = promotion.StartsOnUtc,
                    PromotionEndsOnUtc = promotion.EndsOnUtc
                })
            .ToArrayAsync(cancellationToken);

        var items = requested
            .Select(id =>
            {
                var row = rows.SingleOrDefault(value => value.Id == id);
                if (row is null)
                    return new ConfirmedCouponDto(id, false, "Unknown", nameof(CouponStatus.Disabled), Guid.Empty);

                var reason = row switch
                {
                    { Status: not CouponStatus.Active } => row.Status == CouponStatus.Expired ? "Expired" : "Inactive",
                    _ when row.ValidFromUtc > now => "NotStarted",
                    _ when row.ValidUntilUtc <= now => "Expired",
                    _ when row.AssignedUserId is not null
                           && !string.Equals(row.AssignedUserId, request.UserId, StringComparison.Ordinal)
                        => "AssignedToAnotherUser",
                    { PromotionStatus: not PromotionStatus.Active } => "PromotionInactive",
                    _ when row.PromotionStartsOnUtc > now || row.PromotionEndsOnUtc <= now => "PromotionInactive",
                    _ => null
                };
                return new ConfirmedCouponDto(id, reason is null, reason, row.Status.ToString(), row.PromotionId);
            })
            .ToArray();

        return new ConfirmedCoupons(items);
    }
}
