using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using System.Data;
using System.Data.Common;

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
                throw new InvalidOperationException("Coupon per-user usage limit has been reached.");

            long? bucketId = null;
            if (coupon.HasTotalLimit)
            {
                bucketId = await TryClaimQuotaBucketAsync(coupon.CouponId, orderId, cancellationToken)
                    ?? throw new InvalidOperationException("Coupon quota has been exhausted.");
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
            .FromSqlInterpolated($"""
                SELECT *
                FROM order_coupon_redemptions
                WHERE "OrderId" = {orderId}
                ORDER BY "CouponId"
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);

        foreach (var redemption in redemptions)
        {
            if (redemption.Status != CouponRedemptionStatus.Reserved)
                continue;

            if (redemption.QuotaBucketId.HasValue)
            {
                var affected = consume
                    ? await ExecuteAsync(
                        """
                        UPDATE coupon_quota_buckets
                        SET "ReservedCount" = "ReservedCount" - 1,
                            "ConsumedCount" = "ConsumedCount" + 1,
                            "Version" = "Version" + 1
                        WHERE "Id" = @bucketId AND "ReservedCount" > 0;
                        """,
                        redemption.QuotaBucketId.Value,
                        cancellationToken)
                    : await ExecuteAsync(
                        """
                        UPDATE coupon_quota_buckets
                        SET "ReservedCount" = "ReservedCount" - 1,
                            "Version" = "Version" + 1
                        WHERE "Id" = @bucketId AND "ReservedCount" > 0;
                        """,
                        redemption.QuotaBucketId.Value,
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

    private async Task<long?> ClaimBucketAsync(
        Guid couponId,
        int preferredBucket,
        int bucketCount,
        bool skipLocked,
        CancellationToken cancellationToken)
    {
        var lockClause = skipLocked ? "FOR UPDATE SKIP LOCKED" : "FOR UPDATE";
        var transaction = EnsureTransaction();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $$"""
            WITH candidate AS
            (
                SELECT "Id"
                FROM coupon_quota_buckets
                WHERE "CouponId" = @couponId
                  AND "ReservedCount" + "ConsumedCount" < "Capacity"
                ORDER BY mod("BucketNumber" - @preferredBucket + @bucketCount, @bucketCount)
                {{lockClause}}
                LIMIT 1
            )
            UPDATE coupon_quota_buckets AS bucket
            SET "ReservedCount" = bucket."ReservedCount" + 1,
                "Version" = bucket."Version" + 1
            FROM candidate
            WHERE bucket."Id" = candidate."Id"
              AND bucket."ReservedCount" + bucket."ConsumedCount" < bucket."Capacity"
            RETURNING bucket."Id";
            """;
        AddParameter(command, "couponId", couponId);
        AddParameter(command, "preferredBucket", preferredBucket);
        AddParameter(command, "bucketCount", bucketCount);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private async Task<int> ExecuteAsync(
        string sql,
        long bucketId,
        CancellationToken cancellationToken)
    {
        var transaction = EnsureTransaction();
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "bucketId", bucketId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private IDbContextTransaction EnsureTransaction() =>
        db.Database.CurrentTransaction
        ?? throw new InvalidOperationException("Coupon quota changes require an active order transaction.");

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
