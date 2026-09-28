using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.Persistence.Postgres;

namespace MSA.OrderService.Infrastructure;

internal sealed class PostgresCouponRedemptionStore(OrderDbContext db) : ICouponRedemptionStore
{
    public async Task ReserveAsync(
        Guid orderId,
        string userId,
        IReadOnlyList<AppliedCoupon> coupons,
        DateTimeOffset expiresOnUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var coupon in coupons.OrderBy(item => item.CouponId))
        {
            var activeForUser = await db.CouponRedemptions
                .CountAsync(
                    redemption =>
                        redemption.CouponId == coupon.CouponId
                        && redemption.UserId == userId
                        && redemption.Status != CouponRedemptionStatus.Released,
                    cancellationToken);
            if (activeForUser >= coupon.PerUserLimit)
                throw new CouponRejectedException(CouponRejectionReason.UsageLimitReached, "Coupon per-user usage limit has been reached.", coupon.CouponId);

            long? bucketId = null;
            if (coupon.HasTotalLimit)
            {
                bucketId = await TryClaimQuotaBucketAsync(coupon.CouponId, orderId, cancellationToken)
                    ?? throw new CouponRejectedException(CouponRejectionReason.QuotaExhausted, "Coupon quota has been exhausted.", coupon.CouponId);
            }

            db.CouponRedemptions.Add(new OrderCouponRedemption(
                Guid.NewGuid(),
                orderId,
                coupon.CouponId,
                userId,
                bucketId,
                coupon.MaskedCode,
                coupon.DiscountAmount,
                expiresOnUtc,
                now));
        }
    }

    public Task ConsumeAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken) =>
        CompleteAsync(orderId, consume: true, now, cancellationToken);

    public Task ReleaseAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken) =>
        CompleteAsync(orderId, consume: false, now, cancellationToken);

    private async Task CompleteAsync(
        Guid orderId,
        bool consume,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var redemptions = await db.CouponRedemptions
            .Where(redemption => redemption.OrderId == orderId)
            .OrderBy(redemption => redemption.CouponId)
            .ForUpdate()
            .ToArrayAsync(cancellationToken);

        foreach (var redemption in redemptions)
        {
            if (redemption.Status != CouponRedemptionStatus.Reserved)
                continue;

            if (redemption.QuotaBucketId.HasValue)
            {
                var bucketId = redemption.QuotaBucketId.Value;
                var reserved = db.CouponQuotaBuckets.Where(bucket => bucket.Id == bucketId && bucket.ReservedCount > 0);
                // 计数器就地增减，避免先读后写的丢失更新。
                var affected = consume
                    ? await reserved.ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(bucket => bucket.ReservedCount, bucket => bucket.ReservedCount - 1)
                            .SetProperty(bucket => bucket.ConsumedCount, bucket => bucket.ConsumedCount + 1)
                            .SetProperty(bucket => bucket.Version, bucket => bucket.Version + 1),
                        cancellationToken)
                    : await reserved.ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(bucket => bucket.ReservedCount, bucket => bucket.ReservedCount - 1)
                            .SetProperty(bucket => bucket.Version, bucket => bucket.Version + 1),
                        cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException("Coupon quota counters are inconsistent with the reservation.");
            }

            if (consume)
                redemption.Consume(now);
            else
                redemption.Release(now);
        }
    }

    private async Task<long?> TryClaimQuotaBucketAsync(
        Guid couponId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var bucketCount = await db.CouponQuotaBuckets
            .AsNoTracking()
            .CountAsync(bucket => bucket.CouponId == couponId, cancellationToken);
        if (bucketCount == 0)
            return null;

        var preferredBucket = (int)(BitConverter.ToUInt32(orderId.ToByteArray(), 0) % (uint)bucketCount);
        var claimed = await ClaimBucketAsync(
            couponId,
            preferredBucket,
            bucketCount,
            skipLocked: true,
            cancellationToken);
        return claimed ?? await ClaimBucketAsync(
            couponId,
            preferredBucket,
            bucketCount,
            skipLocked: false,
            cancellationToken);
    }

    /// <summary>
    /// 从首选桶开始找一个还有余量的桶并锁住它，再就地把预占数加一。
    /// 锁与更新在同一个事务里，锁住的那一行不会被别人改走，因此和单条 CTE 语句等价，但可读性好得多。
    /// SKIP LOCKED 先让并发请求分散到不同的桶，全被锁住时再退回排队等待。
    /// </summary>
    private async Task<long?> ClaimBucketAsync(
        Guid couponId,
        int preferredBucket,
        int bucketCount,
        bool skipLocked,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var bucketId = await db.CouponQuotaBuckets
            .Where(bucket => bucket.CouponId == couponId
                && bucket.ReservedCount + bucket.ConsumedCount < bucket.Capacity)
            .OrderBy(bucket => (bucket.BucketNumber - preferredBucket + bucketCount) % bucketCount)
            .Select(bucket => (long?)bucket.Id)
            .WithRowLock(RowLockStrength.Update, skipLocked ? RowLockWait.SkipLocked : RowLockWait.Wait)
            .FirstOrDefaultAsync(cancellationToken);
        if (bucketId is not { } claimed)
            return null;

        var affected = await db.CouponQuotaBuckets
            .Where(bucket => bucket.Id == claimed
                && bucket.ReservedCount + bucket.ConsumedCount < bucket.Capacity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(bucket => bucket.ReservedCount, bucket => bucket.ReservedCount + 1)
                    .SetProperty(bucket => bucket.Version, bucket => bucket.Version + 1),
                cancellationToken);
        return affected == 1 ? claimed : null;
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Coupon quota changes require an active order transaction.");
    }
}
