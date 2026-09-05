using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application.FlashSales;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Integration;

internal sealed class OrderSubmittedHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    IFlashSaleStockStore flashSaleStock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
{
    public async Task HandleAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, nameof(OrderSubmittedHandler), cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] stockCacheSkus = [];
        Guid? flashSaleCacheId = null;
        try
        {
            await transactionLock.AcquireOrderReservationAsync(integrationEvent.OrderId, cancellationToken);
            if (await db.Reservations.AsNoTracking().AnyAsync(
                    reservation => reservation.OrderId == integrationEvent.OrderId,
                    cancellationToken))
            {
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            if (integrationEvent.FlashSaleCampaignId is Guid campaignId)
            {
                flashSaleCacheId = campaignId;
                await ReserveFlashSaleAsync(integrationEvent, campaignId, cancellationToken);
            }
            else
            {
                stockCacheSkus = await ReserveRegularStockAsync(integrationEvent, cancellationToken);
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        foreach (var sku in stockCacheSkus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        if (flashSaleCacheId.HasValue)
            await cache.RemoveFlashSaleAsync(flashSaleCacheId.Value, cancellationToken);
    }

    private async Task ReserveFlashSaleAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.Items.Count != 1)
        {
            PublishRejected(integrationEvent.OrderId, "A flash-sale order must contain exactly one item.");
            return;
        }

        var line = integrationEvent.Items[0];
        var now = clock.GetUtcNow();
        await transactionLock.AcquireFlashSaleBuyerAsync(campaignId, integrationEvent.UserId, cancellationToken);
        var existingBuyerOrderId = await db.Reservations
            .AsNoTracking()
            .Where(reservation =>
                reservation.FlashSaleCampaignId == campaignId
                && reservation.UserId == integrationEvent.UserId)
            .Select(reservation => (Guid?)reservation.OrderId)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingBuyerOrderId.HasValue)
        {
            PublishRejected(integrationEvent.OrderId, "This buyer has already submitted an order for the campaign.");
            return;
        }

        var attempt = await flashSaleStock.TryReserveAsync(
            campaignId,
            line.Sku,
            line.Quantity,
            integrationEvent.OrderId,
            cancellationToken);
        if (!attempt.Succeeded)
        {
            PublishRejected(integrationEvent.OrderId, attempt.RejectionReason ?? "Flash-sale inventory is unavailable.");
            return;
        }

        var reservation = InventoryReservation.CreateFlashSale(
            Guid.NewGuid(),
            integrationEvent.OrderId,
            integrationEvent.UserId,
            campaignId,
            attempt.StockBucketId!.Value,
            line.Sku,
            line.Quantity,
            now);
        db.Reservations.Add(reservation);
        PublishReserved(integrationEvent, reservation.Id);
    }

    private async Task<string[]> ReserveRegularStockAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var requested = integrationEvent.Items
            .GroupBy(line => line.Sku, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(line => line.Quantity),
                StringComparer.Ordinal);
        var skus = requested.Keys.Order(StringComparer.Ordinal).ToArray();

        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        if (requested.Any(pair => !stock.TryGetValue(pair.Key, out var item) || item.Available < pair.Value))
        {
            PublishRejected(integrationEvent.OrderId, "Insufficient inventory.");
            return skus;
        }

        foreach (var pair in requested)
            stock[pair.Key].Reserve(pair.Value);

        var reservation = new InventoryReservation(
            Guid.NewGuid(),
            integrationEvent.OrderId,
            requested.Select(pair => new InventoryReservationLine(pair.Key, pair.Value)),
            clock.GetUtcNow());
        db.Reservations.Add(reservation);
        PublishReserved(integrationEvent, reservation.Id);
        return skus;
    }

    private void PublishReserved(OrderSubmittedIntegrationEvent source, Guid reservationId) =>
        Publish(new InventoryReservedIntegrationEvent
        {
            OrderId = source.OrderId,
            ReservationId = reservationId,
            Amount = source.Amount,
            Currency = source.Currency,
            PaymentMethod = source.PaymentMethod
        });

    private void PublishRejected(Guid orderId, string reason) =>
        Publish(new InventoryRejectedIntegrationEvent { OrderId = orderId, Reason = reason });

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
}

internal sealed class ReleaseInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache) : IIntegrationEventHandler<ReleaseInventoryIntegrationEvent>
{
    public async Task HandleAsync(
        ReleaseInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, nameof(ReleaseInventoryHandler), cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] changedSkus = [];
        Guid? changedCampaignId = null;
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(db, integrationEvent.ReservationId, cancellationToken);
            if (reservation is not null && reservation.Status == ReservationStatus.Reserved)
            {
                changedSkus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
                if (reservation.FlashSaleCampaignId is Guid campaignId)
                {
                    changedCampaignId = campaignId;
                    await ReleaseFlashSaleAsync(reservation, campaignId, cancellationToken);
                }
                else
                {
                    await ReleaseRegularAsync(reservation, cancellationToken);
                }

                reservation.TryRelease();
                Publish(new InventoryReleasedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        foreach (var sku in changedSkus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        if (changedCampaignId.HasValue)
            await cache.RemoveFlashSaleAsync(changedCampaignId.Value, cancellationToken);
    }

    private async Task ReleaseFlashSaleAsync(
        InventoryReservation reservation,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        var campaign = await InventoryRowLocks.LockCampaignAsync(db, campaignId, cancellationToken);
        var bucket = await InventoryRowLocks.LockBucketAsync(db, reservation.FlashSaleStockBucketId!.Value, cancellationToken);
        var line = reservation.Lines.Single();
        var campaignClosed = campaign.Status == FlashSaleCampaignStatus.Closed;

        bucket.Release(line.Quantity, campaignClosed);
        if (!campaignClosed)
            return;

        await transactionLock.AcquireStockItemsAsync([line.Sku], cancellationToken);
        var stockItem = await db.StockItems.SingleAsync(item => item.Sku == line.Sku, cancellationToken);
        stockItem.Release(line.Quantity);
    }

    private async Task ReleaseRegularAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken)
    {
        var skus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        foreach (var line in reservation.Lines)
            stock[line.Sku].Release(line.Quantity);
    }

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
}

internal sealed class CommitInventoryHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache) : IIntegrationEventHandler<CommitInventoryIntegrationEvent>
{
    public async Task HandleAsync(
        CommitInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, nameof(CommitInventoryHandler), cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] changedSkus = [];
        Guid? changedCampaignId = null;
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(db, integrationEvent.ReservationId, cancellationToken);
            if (reservation is not null && reservation.Status == ReservationStatus.Reserved)
            {
                changedSkus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
                if (reservation.FlashSaleCampaignId is Guid campaignId)
                {
                    changedCampaignId = campaignId;
                    await CommitFlashSaleAsync(reservation, campaignId, cancellationToken);
                }
                else
                {
                    await CommitRegularAsync(reservation, cancellationToken);
                }

                reservation.TryCommit();
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        foreach (var sku in changedSkus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        if (changedCampaignId.HasValue)
            await cache.RemoveFlashSaleAsync(changedCampaignId.Value, cancellationToken);
    }

    private async Task CommitFlashSaleAsync(
        InventoryReservation reservation,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        _ = await InventoryRowLocks.LockCampaignAsync(db, campaignId, cancellationToken);
        var bucket = await InventoryRowLocks.LockBucketAsync(db, reservation.FlashSaleStockBucketId!.Value, cancellationToken);
        var line = reservation.Lines.Single();
        bucket.Commit(line.Quantity);

        await transactionLock.AcquireStockItemsAsync([line.Sku], cancellationToken);
        var stockItem = await db.StockItems.SingleAsync(item => item.Sku == line.Sku, cancellationToken);
        stockItem.Commit(line.Quantity);
    }

    private async Task CommitRegularAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken)
    {
        var skus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        foreach (var line in reservation.Lines)
            stock[line.Sku].Commit(line.Quantity);
    }
}

internal static class InventoryRowLocks
{
    public static async Task<InventoryReservation?> LockReservationAsync(
        InventoryDbContext db,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations
            .FromSqlInterpolated($"SELECT * FROM inventory_reservations WHERE \"Id\" = {reservationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (reservation is not null)
            await db.Entry(reservation).Collection(value => value.Lines).LoadAsync(cancellationToken);
        return reservation;
    }

    public static async Task<FlashSaleCampaign> LockCampaignAsync(
        InventoryDbContext db,
        Guid campaignId,
        CancellationToken cancellationToken) =>
        await db.FlashSaleCampaigns
            .FromSqlInterpolated($"SELECT * FROM flash_sale_campaigns WHERE \"Id\" = {campaignId} FOR UPDATE")
            .SingleAsync(cancellationToken);

    public static Task<FlashSaleStockBucket> LockBucketAsync(
        InventoryDbContext db,
        long bucketId,
        CancellationToken cancellationToken) =>
        db.FlashSaleStockBuckets
            .FromSqlInterpolated($"SELECT * FROM flash_sale_stock_buckets WHERE \"Id\" = {bucketId} FOR UPDATE")
            .SingleAsync(cancellationToken);
}
