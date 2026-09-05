using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MSA.InventoryService.Application.FlashSales;
using MSA.InventoryService.Domain;
using System.Data;
using System.Data.Common;

namespace MSA.InventoryService.Infrastructure;

internal sealed class PostgresFlashSaleStockStore(
    InventoryDbContext db,
    IOptions<FlashSaleOptions> options,
    TimeProvider clock) : IFlashSaleStockStore
{
    public async Task<FlashSaleReservationAttempt> TryReserveAsync(
        Guid campaignId,
        string sku,
        int quantity,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var now = await GetDatabaseNowAsync(cancellationToken);
        var campaign = await db.FlashSaleCampaigns
            .AsNoTracking()
            .Where(value => value.Id == campaignId)
            .Select(value => new
            {
                value.Sku,
                value.PurchaseLimit,
                value.Strategy,
                value.Status,
                value.StartsOnUtc,
                value.EndsOnUtc,
                value.BucketCount
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (campaign is null)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign was not found.");
        if (!string.Equals(campaign.Sku, sku, StringComparison.Ordinal))
            return FlashSaleReservationAttempt.Rejected("The requested SKU does not belong to this flash-sale campaign.");
        if (quantity <= 0 || quantity > campaign.PurchaseLimit)
            return FlashSaleReservationAttempt.Rejected("The requested quantity exceeds the campaign purchase limit.");
        if (campaign.Status == FlashSaleCampaignStatus.Closed)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign is closed.");
        if (now < campaign.StartsOnUtc)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign has not started.");
        if (now >= campaign.EndsOnUtc)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign has ended.");

        var preferredBucket = GetPreferredBucket(orderId, campaign.BucketCount);
        if (campaign.Strategy == FlashSaleStockStrategy.SingleRow)
        {
            var bucketId = await ReserveSingleRowAsync(campaignId, quantity, now, cancellationToken);
            return bucketId.HasValue
                ? FlashSaleReservationAttempt.Reserved(bucketId.Value)
                : await GetRejectionAsync(campaignId, quantity, now, cancellationToken);
        }

        for (var attempt = 0; attempt < options.Value.ReservationSkipLockedRetries; attempt++)
        {
            var bucketId = await ReserveBucketedAsync(
                campaignId,
                quantity,
                preferredBucket,
                now,
                skipLocked: true,
                cancellationToken);
            if (bucketId.HasValue)
                return FlashSaleReservationAttempt.Reserved(bucketId.Value);

            await Task.Delay(TimeSpan.FromMilliseconds(1 << attempt), clock, cancellationToken);
        }

        var fallbackBucketId = await ReserveBucketedAsync(
            campaignId,
            quantity,
            preferredBucket,
            now,
            skipLocked: false,
            cancellationToken);
        if (fallbackBucketId.HasValue)
            return FlashSaleReservationAttempt.Reserved(fallbackBucketId.Value);

        // A bucketed pool can become fragmented even when its aggregate available
        // quantity is sufficient. This locked slow path compacts only available
        // allocation, then reserves from one bucket so the existing reservation
        // model and fast path remain unchanged.
        var compactedBucketId = await ReserveFragmentedPoolAsync(
            campaignId,
            quantity,
            preferredBucket,
            now,
            cancellationToken);
        return compactedBucketId.HasValue
            ? FlashSaleReservationAttempt.Reserved(compactedBucketId.Value)
            : await GetRejectionAsync(campaignId, quantity, now, cancellationToken);
    }

    private Task<long?> ReserveSingleRowAsync(
        Guid campaignId,
        int quantity,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteReservationAsync(
            """
            UPDATE flash_sale_stock_buckets AS bucket
            SET "ReservedQuantity" = bucket."ReservedQuantity" + @quantity,
                "Version" = bucket."Version" + 1
            FROM flash_sale_campaigns AS campaign
            WHERE bucket."CampaignId" = @campaignId
              AND bucket."BucketNumber" = 0
              AND campaign."Id" = bucket."CampaignId"
              AND campaign."Status" = 0
              AND campaign."StartsOnUtc" <= @now
              AND campaign."EndsOnUtc" > @now
              AND bucket."AllocatedQuantity" - bucket."ReservedQuantity" - bucket."SoldQuantity" - bucket."ReleasedQuantity" >= @quantity
            RETURNING bucket."Id";
            """,
            campaignId,
            quantity,
            now,
            preferredBucket: null,
            cancellationToken);

    private Task<long?> ReserveBucketedAsync(
        Guid campaignId,
        int quantity,
        int preferredBucket,
        DateTimeOffset now,
        bool skipLocked,
        CancellationToken cancellationToken)
    {
        var lockClause = skipLocked ? "FOR UPDATE OF bucket SKIP LOCKED" : "FOR UPDATE OF bucket";
        var sql = $$"""
            WITH candidate AS
            (
                SELECT bucket."Id"
                FROM flash_sale_stock_buckets AS bucket
                INNER JOIN flash_sale_campaigns AS campaign ON campaign."Id" = bucket."CampaignId"
                WHERE campaign."Id" = @campaignId
                  AND campaign."Status" = 0
                  AND campaign."StartsOnUtc" <= @now
                  AND campaign."EndsOnUtc" > @now
                  AND bucket."AllocatedQuantity" - bucket."ReservedQuantity" - bucket."SoldQuantity" - bucket."ReleasedQuantity" >= @quantity
                ORDER BY mod(bucket."BucketNumber" - @preferredBucket + campaign."BucketCount", campaign."BucketCount")
                {{lockClause}}
                LIMIT 1
            )
            UPDATE flash_sale_stock_buckets AS bucket
            SET "ReservedQuantity" = bucket."ReservedQuantity" + @quantity,
                "Version" = bucket."Version" + 1
            FROM candidate
            WHERE bucket."Id" = candidate."Id"
              AND bucket."AllocatedQuantity" - bucket."ReservedQuantity" - bucket."SoldQuantity" - bucket."ReleasedQuantity" >= @quantity
            RETURNING bucket."Id";
            """;

        return ExecuteReservationAsync(
            sql,
            campaignId,
            quantity,
            now,
            preferredBucket,
            cancellationToken);
    }

    private async Task<long?> ExecuteReservationAsync(
        string sql,
        Guid campaignId,
        int quantity,
        DateTimeOffset now,
        int? preferredBucket,
        CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Flash-sale reservation requires an active database transaction.");
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        AddParameter(command, "campaignId", campaignId);
        AddParameter(command, "quantity", quantity);
        AddParameter(command, "now", now);
        if (preferredBucket.HasValue)
            AddParameter(command, "preferredBucket", preferredBucket.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private async Task<long?> ReserveFragmentedPoolAsync(
        Guid campaignId,
        int quantity,
        int preferredBucket,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var campaign = await db.FlashSaleCampaigns
            .FromSqlInterpolated($"SELECT * FROM flash_sale_campaigns WHERE \"Id\" = {campaignId} AND \"Status\" = 0 AND \"StartsOnUtc\" <= {now} AND \"EndsOnUtc\" > {now} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (campaign is null)
            return null;

        var buckets = await db.FlashSaleStockBuckets
            .FromSqlInterpolated($"SELECT * FROM flash_sale_stock_buckets WHERE \"CampaignId\" = {campaignId} ORDER BY \"BucketNumber\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (buckets.Sum(bucket => bucket.AvailableQuantity) < quantity)
            return null;

        var orderedBuckets = buckets
            .OrderBy(
                bucket => (bucket.BucketNumber - preferredBucket + campaign.BucketCount)
                    % campaign.BucketCount)
            .ToArray();
        var target = orderedBuckets
            .OrderByDescending(bucket => bucket.AvailableQuantity)
            .ThenBy(
                bucket => (bucket.BucketNumber - preferredBucket + campaign.BucketCount)
                    % campaign.BucketCount)
            .First();

        var missingQuantity = quantity - target.AvailableQuantity;
        foreach (var donor in orderedBuckets
                     .Where(bucket => !ReferenceEquals(bucket, target) && bucket.AvailableQuantity > 0)
                     .OrderByDescending(bucket => bucket.AvailableQuantity))
        {
            if (missingQuantity <= 0)
                break;

            var transferQuantity = Math.Min(donor.AvailableQuantity, missingQuantity);
            donor.TransferAvailableTo(target, transferQuantity);
            missingQuantity -= transferQuantity;
        }

        if (missingQuantity > 0 || !target.TryReserve(quantity))
            throw new InvalidOperationException("Flash-sale bucket compaction did not preserve available inventory.");

        return target.Id;
    }

    private async Task<FlashSaleReservationAttempt> GetRejectionAsync(
        Guid campaignId,
        int quantity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = await db.FlashSaleCampaigns
            .AsNoTracking()
            .Where(campaign => campaign.Id == campaignId)
            .Select(campaign => new
            {
                campaign.Status,
                campaign.StartsOnUtc,
                campaign.EndsOnUtc,
                LargestAvailableBucket = campaign.StockBuckets.Max(bucket =>
                    bucket.AllocatedQuantity - bucket.ReservedQuantity - bucket.SoldQuantity - bucket.ReleasedQuantity)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign was not found.");
        if (state.Status == FlashSaleCampaignStatus.Closed)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign is closed.");
        if (now < state.StartsOnUtc)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign has not started.");
        if (now >= state.EndsOnUtc)
            return FlashSaleReservationAttempt.Rejected("Flash-sale campaign has ended.");
        if (state.LargestAvailableBucket < quantity)
            return FlashSaleReservationAttempt.Rejected("Flash-sale inventory is sold out.");
        return FlashSaleReservationAttempt.Rejected("Flash-sale inventory is temporarily busy; retry with the same idempotency key.");
    }

    private static int GetPreferredBucket(Guid orderId, int bucketCount)
    {
        var hash = BitConverter.ToUInt32(orderId.ToByteArray(), 0);
        return (int)(hash % (uint)bucketCount);
    }

    private async Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Flash-sale reservation requires an active database transaction.");
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT CURRENT_TIMESTAMP;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("PostgreSQL did not return its current timestamp.")
        };
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
