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

        var strategy = PromotionDefinitionMapper.ParseEnum<CouponQuotaStrategy>(
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

internal sealed class CreateCouponBatchHandler(
    IPromotionRepository promotions,
    ICouponRepository coupons,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreateCouponBatchCommand, IReadOnlyList<IssuedCouponDto>>
{
    public async Task<IReadOnlyList<IssuedCouponDto>> Handle(CreateCouponBatchCommand request, CancellationToken cancellationToken)
    {
        var promotion = await promotions.FindAsync(request.PromotionId, cancellationToken)
            ?? throw new KeyNotFoundException("Coupon promotion was not found.");
        if (promotion.TriggerType != PromotionTriggerType.Coupon)
            throw new InvalidOperationException("Coupons can only reference a Coupon-triggered promotion.");
        if (request.ValidFromUtc < promotion.StartsOnUtc || request.ValidUntilUtc > promotion.EndsOnUtc)
            throw new InvalidOperationException("Coupon validity must be contained by the promotion window.");
        if (request.Activate && promotion.Status != PromotionStatus.Active)
            throw new InvalidOperationException("The coupon promotion must be published and active before coupons can be activated.");

        var owners = request.AssignedUserIds is { Count: > 0 } assigned
            ? assigned.Select(user => (string?)user.Trim()).ToArray()
            : Enumerable.Repeat<string?>(null, request.Count).ToArray();
        var codes = await GenerateUniqueCodesAsync(request.CodePrefix, owners.Length, cancellationToken);
        const CouponQuotaStrategy strategy = CouponQuotaStrategy.SingleRow;
        var now = clock.GetUtcNow();
        var issued = new List<IssuedCouponDto>(owners.Length);
        for (var index = 0; index < owners.Length; index++)
        {
            var coupon = new Coupon(
                Guid.NewGuid(),
                request.PromotionId,
                codes[index],
                request.ValidFromUtc,
                request.ValidUntilUtc,
                owners[index],
                request.TotalLimitPerCoupon,
                request.PerUserLimit,
                strategy,
                bucketCount: null,
                request.Actor,
                now);
            coupons.Add(coupon);
            if (request.Activate)
            {
                coupon.Activate(request.Actor, now);
                snapshots.Publish(coupon);
            }
            issued.Add(new IssuedCouponDto(coupon.Id, codes[index], coupon.MaskedCode, coupon.AssignedUserId));
        }

        audit.Record("Promotion", promotion.Id, "CouponBatchIssued", request.Actor, now, new
        {
            Count = issued.Count,
            Assigned = owners.Count(owner => owner is not null),
            request.Activate
        });
        return issued;
    }

    private async Task<string[]> GenerateUniqueCodesAsync(string? prefix, int count, CancellationToken cancellationToken)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        // Collisions are astronomically rare with 10 random characters, but a few retries keep the batch exact.
        for (var attempt = 0; attempt < 5 && codes.Count < count; attempt++)
        {
            var candidates = Enumerable.Range(0, count - codes.Count)
                .Select(_ => Coupon.GenerateCode(prefix))
                .Where(code => !codes.Contains(code))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var taken = await coupons.FindExistingDigestsAsync(
                candidates.Select(Coupon.ComputeCodeDigest).ToArray(),
                cancellationToken);
            foreach (var code in candidates.Where(code => !taken.Contains(Coupon.ComputeCodeDigest(code))))
                codes.Add(code);
        }
        if (codes.Count < count)
            throw new InvalidOperationException("Could not generate enough unique coupon codes; try again.");
        return codes.ToArray();
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
        var coupon = await coupons.GetRequiredAsync(request.CouponId, cancellationToken);
        coupon.EnsureVersion(request.ExpectedVersion);
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
        var coupon = await coupons.GetRequiredAsync(request.CouponId, cancellationToken);
        coupon.EnsureVersion(request.ExpectedVersion);
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
        var coupon = await coupons.GetRequiredAsync(request.CouponId, cancellationToken);
        coupon.EnsureVersion(request.ExpectedVersion);
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
            request.CampaignId,
            cancellationToken);
}
