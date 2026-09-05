using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Application;

internal sealed class CreateCouponHandler(
    IPromotionRepository promotions,
    ICouponRepository coupons,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateCouponCommand, Guid>
{
    public async Task<Guid> Handle(CreateCouponCommand request, CancellationToken cancellationToken)
    {
        var promotion = await promotions.FindAsync(request.PromotionId, cancellationToken)
            ?? throw new KeyNotFoundException("Coupon promotion was not found.");
        if (promotion.TriggerType != PromotionTriggerType.Coupon)
            throw new InvalidOperationException("Coupons can only reference a Coupon-triggered promotion.");
        if (request.ValidFromUtc < promotion.StartsOnUtc || request.ValidUntilUtc > promotion.EndsOnUtc)
            throw new InvalidOperationException("Coupon validity must be contained by the promotion window.");

        var digest = Coupon.ComputeCodeDigest(request.Code);
        if (await coupons.CodeDigestExistsAsync(digest, null, cancellationToken))
            throw new InvalidOperationException("Coupon code already exists.");

        var strategy = CreatePromotionHandler.ParseEnum<CouponQuotaStrategy>(
            request.QuotaStrategy,
            nameof(request.QuotaStrategy));
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var coupon = new Coupon(
            id,
            request.PromotionId,
            request.Code,
            request.ValidFromUtc,
            request.ValidUntilUtc,
            request.AssignedUserId,
            request.TotalLimit,
            request.PerUserLimit,
            strategy,
            request.BucketCount,
            request.Actor,
            now);
        coupons.Add(coupon);
        audit.Record("Coupon", id, "DraftCreated", request.Actor, now, new
        {
            coupon.PromotionId,
            coupon.MaskedCode,
            coupon.TotalLimit,
            coupon.PerUserLimit,
            coupon.QuotaStrategy
        });
        return id;
    }
}

internal sealed class ActivateCouponHandler(
    IPromotionRepository promotions,
    ICouponRepository coupons,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ActivateCouponCommand, bool>
{
    public async Task<bool> Handle(ActivateCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await CouponHandlerHelpers.GetAsync(coupons, request.CouponId, cancellationToken);
        CouponHandlerHelpers.EnsureVersion(coupon.Version, request.ExpectedVersion);
        var promotion = await promotions.FindAsync(coupon.PromotionId, cancellationToken)
            ?? throw new KeyNotFoundException("Coupon promotion was not found.");
        if (promotion.TriggerType != PromotionTriggerType.Coupon || promotion.Status != PromotionStatus.Active)
            throw new InvalidOperationException("The coupon promotion must be published and active first.");
        if (coupon.ValidFromUtc < promotion.StartsOnUtc || coupon.ValidUntilUtc > promotion.EndsOnUtc)
            throw new InvalidOperationException("Coupon validity must be contained by the promotion window.");

        var now = clock.GetUtcNow();
        coupon.Activate(request.Actor, now);
        snapshots.Publish(coupon);
        audit.Record("Coupon", coupon.Id, "Activated", request.Actor, now, new
        {
            coupon.Revision,
            BucketCapacity = coupon.QuotaBuckets.Sum(bucket => bucket.Capacity)
        });
        return true;
    }
}

internal sealed class DisableCouponHandler(
    ICouponRepository coupons,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<DisableCouponCommand, bool>
{
    public async Task<bool> Handle(DisableCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await CouponHandlerHelpers.GetAsync(coupons, request.CouponId, cancellationToken);
        CouponHandlerHelpers.EnsureVersion(coupon.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        coupon.Disable(request.Actor, now);
        snapshots.Publish(coupon);
        audit.Record("Coupon", coupon.Id, "Disabled", request.Actor, now, new { request.Reason });
        return true;
    }
}

internal sealed class ExpireCouponHandler(
    ICouponRepository coupons,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ExpireCouponCommand, bool>
{
    public async Task<bool> Handle(ExpireCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await CouponHandlerHelpers.GetAsync(coupons, request.CouponId, cancellationToken);
        CouponHandlerHelpers.EnsureVersion(coupon.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        coupon.Expire(request.Actor, now);
        snapshots.Publish(coupon);
        audit.Record("Coupon", coupon.Id, "Expired", request.Actor, now);
        return true;
    }
}

internal sealed class GetCouponHandler(ICatalogReadService reads)
    : IRequestHandler<GetCouponQuery, CouponDto?>
{
    public Task<CouponDto?> Handle(GetCouponQuery request, CancellationToken cancellationToken) =>
        reads.GetCouponAsync(request.CouponId, cancellationToken);
}

internal sealed class ListCouponsHandler(ICatalogReadService reads)
    : IRequestHandler<ListCouponsQuery, PagedResult<CouponDto>>
{
    public Task<PagedResult<CouponDto>> Handle(ListCouponsQuery request, CancellationToken cancellationToken) =>
        reads.ListCouponsAsync(
            request.Skip,
            request.Take,
            request.Status,
            request.PromotionId,
            cancellationToken);
}

file static class CouponHandlerHelpers
{
    public static async Task<Coupon> GetAsync(
        ICouponRepository coupons,
        Guid couponId,
        CancellationToken cancellationToken) =>
        await coupons.FindAsync(couponId, cancellationToken)
        ?? throw new KeyNotFoundException("Coupon was not found.");

    public static void EnsureVersion(long current, long? expected)
    {
        if (expected.HasValue && expected.Value != current)
            throw new InvalidOperationException($"Coupon version mismatch. Expected {expected}, current {current}.");
    }
}
