using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application;
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
    IOptions<InventoryReservationOptions> reservationOptions,
    TimeProvider clock) : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
{
    private const string ConsumerId = "inventory.order-submitted.v1";

    public async Task HandleAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var requestHash = InventoryRequestHasher.Compute(integrationEvent);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] stockCacheSkus = [];
        Guid? flashSaleCacheId = null;
        try
        {
            await transactionLock.AcquireOrderReservationAsync(integrationEvent.OrderId, cancellationToken);
            var existingDecision = await db.RequestDecisions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    decision => decision.OrderId == integrationEvent.OrderId,
                    cancellationToken);
            if (existingDecision is not null)
            {
                existingDecision.EnsureMatches(requestHash);
                ReplayDecision(integrationEvent, existingDecision);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            // 兼容决策流水引入前创建的预占；订单锁保证该补登记路径只执行一次且无竞态。
            var legacyReservationId = await db.Reservations
                .AsNoTracking()
                .Where(reservation => reservation.OrderId == integrationEvent.OrderId)
                .Select(reservation => (Guid?)reservation.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (legacyReservationId.HasValue)
            {
                var adoptedDecision = InventoryRequestDecision.Accept(
                    integrationEvent.OrderId,
                    requestHash,
                    legacyReservationId.Value,
                    clock.GetUtcNow());
                db.RequestDecisions.Add(adoptedDecision);
                ReplayDecision(integrationEvent, adoptedDecision);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            ReservationAttempt attempt;
            if (integrationEvent.FlashSaleCampaignId is Guid campaignId)
            {
                attempt = await ReserveFlashSaleAsync(integrationEvent, campaignId, cancellationToken);
            }
            else
            {
                attempt = await ReserveRegularStockAsync(integrationEvent, cancellationToken);
            }

            var decidedOnUtc = clock.GetUtcNow();
            if (attempt.Reservation is not null)
            {
                db.RequestDecisions.Add(InventoryRequestDecision.Accept(
                    integrationEvent.OrderId,
                    requestHash,
                    attempt.Reservation.Id,
                    decidedOnUtc));
                PublishReserved(integrationEvent, attempt.Reservation.Id);
            }
            else
            {
                var rejectionReason = attempt.RejectionReason
                    ?? throw new InvalidOperationException("A rejected inventory attempt requires a reason.");
                db.RequestDecisions.Add(InventoryRequestDecision.Reject(
                    integrationEvent.OrderId,
                    requestHash,
                    rejectionReason,
                    decidedOnUtc));
                PublishRejected(integrationEvent.OrderId, rejectionReason);
            }

            stockCacheSkus = attempt.ChangedSkus;
            flashSaleCacheId = attempt.ChangedCampaignId;
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

    private async Task<ReservationAttempt> ReserveFlashSaleAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.Items.Count != 1)
            return ReservationAttempt.Rejected("A flash-sale order must contain exactly one item.");

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
            return ReservationAttempt.Rejected("This buyer has already submitted an order for the campaign.");

        var attempt = await flashSaleStock.TryReserveAsync(
            campaignId,
            line.Sku,
            line.Quantity,
            integrationEvent.OrderId,
            cancellationToken);
        if (!attempt.Succeeded)
            return ReservationAttempt.Rejected(
                attempt.RejectionReason ?? "Flash-sale inventory is unavailable.");

        var expiresOnUtc = ResolveExpiry(
            integrationEvent.PaymentDueOnUtc,
            now,
            reservationOptions.Value.FlashSaleLifetime);
        var reservation = InventoryReservation.CreateFlashSale(
            Guid.NewGuid(),
            integrationEvent.OrderId,
            integrationEvent.UserId,
            campaignId,
            attempt.StockBucketId!.Value,
            line.Sku,
            line.Quantity,
            now,
            expiresOnUtc);
        db.Reservations.Add(reservation);
        return ReservationAttempt.Accepted(reservation, changedCampaignId: campaignId);
    }

    private async Task<ReservationAttempt> ReserveRegularStockAsync(
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
            return ReservationAttempt.Rejected("Insufficient inventory.");

        var reservationId = Guid.NewGuid();
        var now = clock.GetUtcNow();
        foreach (var pair in requested)
        {
            stock[pair.Key].Reserve(pair.Value);
            db.StockMovements.Add(StockMovement.Record(
                stock[pair.Key],
                StockMovementType.InventoryReserved,
                onHandDelta: 0,
                reservedDelta: pair.Value,
                StockMovementReferenceType.Reservation,
                reservationId,
                "Inventory reserved for an order.",
                now));
        }

        var reservation = new InventoryReservation(
            reservationId,
            integrationEvent.OrderId,
            requested.Select(pair => new InventoryReservationLine(pair.Key, pair.Value)),
            now,
            ResolveExpiry(
                integrationEvent.PaymentDueOnUtc,
                now,
                reservationOptions.Value.RegularLifetime));
        db.Reservations.Add(reservation);
        return ReservationAttempt.Accepted(reservation, changedSkus: skus);
    }

    private void ReplayDecision(
        OrderSubmittedIntegrationEvent source,
        InventoryRequestDecision decision)
    {
        if (decision.IsAccepted)
        {
            PublishReserved(
                source,
                decision.ReservationId
                ?? throw new InvalidOperationException("An accepted inventory decision is missing its reservation id."));
            return;
        }

        PublishRejected(
            source.OrderId,
            decision.RejectionReason
            ?? throw new InvalidOperationException("A rejected inventory decision is missing its reason."));
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

    private static DateTimeOffset ResolveExpiry(
        DateTimeOffset? requestedExpiry,
        DateTimeOffset now,
        TimeSpan fallbackLifetime) =>
        requestedExpiry.HasValue && requestedExpiry.Value > now
            ? requestedExpiry.Value
            : now.Add(fallbackLifetime);

    private sealed record ReservationAttempt(
        InventoryReservation? Reservation,
        string? RejectionReason,
        string[] ChangedSkus,
        Guid? ChangedCampaignId)
    {
        public static ReservationAttempt Accepted(
            InventoryReservation reservation,
            string[]? changedSkus = null,
            Guid? changedCampaignId = null) =>
            new(reservation, null, changedSkus ?? [], changedCampaignId);

        public static ReservationAttempt Rejected(string reason) =>
            new(null, reason, [], null);
    }
}

internal sealed class ReleaseInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IIntegrationEventHandler<ReleaseInventoryIntegrationEvent>
{
    private const string ConsumerId = "inventory.release-requested.v1";

    public async Task HandleAsync(
        ReleaseInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] changedSkus = [];
        Guid? changedCampaignId = null;
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(
                db,
                integrationEvent.ReservationId,
                integrationEvent.OrderId,
                cancellationToken);
            if (reservation.Status == ReservationStatus.Reserved)
            {
                var now = clock.GetUtcNow();
                changedSkus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
                if (reservation.FlashSaleCampaignId is Guid campaignId)
                {
                    changedCampaignId = campaignId;
                    await ReleaseFlashSaleAsync(
                        reservation,
                        campaignId,
                        integrationEvent.Reason,
                        now,
                        cancellationToken);
                }
                else
                {
                    await ReleaseRegularAsync(
                        reservation,
                        integrationEvent.Reason,
                        now,
                        cancellationToken);
                }

                reservation.TryRelease(now, integrationEvent.Reason);
                Publish(new InventoryReleasedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
            }
            else if (reservation.Status == ReservationStatus.Released)
            {
                // 语义重试可能携带新的消息标识；重发持久化决策，使订单 Saga 能从丢失的确认中恢复。
                Publish(new InventoryReleasedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
            }
            else
            {
                throw new InvalidOperationException("Committed inventory cannot be released.");
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
        string reason,
        DateTimeOffset occurredOnUtc,
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
        db.StockMovements.Add(StockMovement.Record(
            stockItem,
            StockMovementType.InventoryReleased,
            onHandDelta: 0,
            reservedDelta: -line.Quantity,
            StockMovementReferenceType.Reservation,
            reservation.Id,
            reason,
            occurredOnUtc));
    }

    private async Task ReleaseRegularAsync(
        InventoryReservation reservation,
        string reason,
        DateTimeOffset occurredOnUtc,
        CancellationToken cancellationToken)
    {
        var skus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        foreach (var line in reservation.Lines)
        {
            stock[line.Sku].Release(line.Quantity);
            db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryReleased,
                onHandDelta: 0,
                reservedDelta: -line.Quantity,
                StockMovementReferenceType.Reservation,
                reservation.Id,
                reason,
                occurredOnUtc));
        }
    }

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
}

internal sealed class CommitInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IIntegrationEventHandler<CommitInventoryIntegrationEvent>
{
    private const string ConsumerId = "inventory.commit-requested.v1";

    public async Task HandleAsync(
        CommitInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
        {
            await db.RollbackTransactionAsync(cancellationToken);
            return;
        }

        string[] changedSkus = [];
        Guid? changedCampaignId = null;
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(
                db,
                integrationEvent.ReservationId,
                integrationEvent.OrderId,
                cancellationToken);
            if (reservation.Status == ReservationStatus.Reserved)
            {
                var now = clock.GetUtcNow();
                changedSkus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
                if (reservation.FlashSaleCampaignId is Guid campaignId)
                {
                    changedCampaignId = campaignId;
                    await CommitFlashSaleAsync(reservation, campaignId, now, cancellationToken);
                }
                else
                {
                    await CommitRegularAsync(reservation, now, cancellationToken);
                }

                reservation.TryCommit(now);
                var committed = new InventoryCommittedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                };
                outbox.Add(OutboxMessage.Create(
                    committed.GetType().AssemblyQualifiedName!,
                    JsonSerializer.Serialize(committed, committed.GetType())));
            }
            else if (reservation.Status == ReservationStatus.Committed)
            {
                var committed = new InventoryCommittedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                };
                outbox.Add(OutboxMessage.Create(
                    committed.GetType().AssemblyQualifiedName!,
                    JsonSerializer.Serialize(committed, committed.GetType())));
            }
            else
            {
                throw new InvalidOperationException("Released inventory cannot be committed.");
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
        DateTimeOffset occurredOnUtc,
        CancellationToken cancellationToken)
    {
        _ = await InventoryRowLocks.LockCampaignAsync(db, campaignId, cancellationToken);
        var bucket = await InventoryRowLocks.LockBucketAsync(db, reservation.FlashSaleStockBucketId!.Value, cancellationToken);
        var line = reservation.Lines.Single();
        bucket.Commit(line.Quantity);

        await transactionLock.AcquireStockItemsAsync([line.Sku], cancellationToken);
        var stockItem = await db.StockItems.SingleAsync(item => item.Sku == line.Sku, cancellationToken);
        stockItem.Commit(line.Quantity);
        db.StockMovements.Add(StockMovement.Record(
            stockItem,
            StockMovementType.InventoryCommitted,
            onHandDelta: -line.Quantity,
            reservedDelta: -line.Quantity,
            StockMovementReferenceType.Reservation,
            reservation.Id,
            "Reserved inventory committed after payment.",
            occurredOnUtc));
    }

    private async Task CommitRegularAsync(
        InventoryReservation reservation,
        DateTimeOffset occurredOnUtc,
        CancellationToken cancellationToken)
    {
        var skus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        foreach (var line in reservation.Lines)
        {
            stock[line.Sku].Commit(line.Quantity);
            db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryCommitted,
                onHandDelta: -line.Quantity,
                reservedDelta: -line.Quantity,
                StockMovementReferenceType.Reservation,
                reservation.Id,
                "Reserved inventory committed after payment.",
                occurredOnUtc));
        }
    }
}

internal static class InventoryRowLocks
{
    public static async Task<InventoryReservation> LockReservationAsync(
        InventoryDbContext db,
        Guid reservationId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations
            .FromSqlInterpolated($"SELECT * FROM inventory_reservations WHERE \"Id\" = {reservationId} AND \"OrderId\" = {orderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Inventory reservation {reservationId} was not found for order {orderId}.");
        await db.Entry(reservation).Collection(value => value.Lines).LoadAsync(cancellationToken);
        return reservation;
    }

    public static async Task<FlashSaleCampaign> LockCampaignAsync(
        InventoryDbContext db,
        Guid campaignId,
        CancellationToken cancellationToken) =>
        await db.FlashSaleCampaigns
            .FromSqlInterpolated($"SELECT * FROM inventory_flash_sale_campaigns WHERE \"Id\" = {campaignId} FOR UPDATE")
            .SingleAsync(cancellationToken);

    public static Task<FlashSaleStockBucket> LockBucketAsync(
        InventoryDbContext db,
        long bucketId,
        CancellationToken cancellationToken) =>
        db.FlashSaleStockBuckets
            .FromSqlInterpolated($"SELECT * FROM inventory_flash_sale_stock_buckets WHERE \"Id\" = {bucketId} FOR UPDATE")
            .SingleAsync(cancellationToken);
}
