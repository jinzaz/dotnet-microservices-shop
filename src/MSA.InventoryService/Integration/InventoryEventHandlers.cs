using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;

namespace MSA.InventoryService.Integration;

/// <summary>
/// Inbox de-duplication, one local transaction and post-commit cache eviction for every inventory message.
/// </summary>
internal abstract class InventoryIntegrationEventHandler<TEvent>(
    InventoryDbContext db,
    IOutboxStore outbox,
    InventoryCacheInvalidator cache,
    string consumerId) : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(integrationEvent.Id, consumerId, cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await HandleCoreAsync(integrationEvent, cancellationToken);
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            cache.Discard();
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await cache.FlushAsync();
    }

    protected InventoryDbContext Db => db;
    protected InventoryCacheInvalidator Cache => cache;

    protected abstract Task HandleCoreAsync(TEvent integrationEvent, CancellationToken cancellationToken);

    protected void Publish(IntegrationEvent integrationEvent) => outbox.AddIntegrationEvent(integrationEvent);
}

internal sealed class OrderSubmittedHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    IOptions<InventoryReservationOptions> reservationOptions,
    TimeProvider clock)
    : InventoryIntegrationEventHandler<OrderSubmittedIntegrationEvent>(db, outbox, cache, "inventory.order-submitted.v2")
{
    protected override async Task HandleCoreAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var requestHash = InventoryRequestHasher.Compute(integrationEvent);
        await transactionLock.AcquireOrderReservationAsync(integrationEvent.OrderId, cancellationToken);
        var existingDecision = await Db.RequestDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == integrationEvent.OrderId, cancellationToken);
        if (existingDecision is not null)
        {
            existingDecision.EnsureMatches(requestHash);
            ReplayDecision(integrationEvent, existingDecision);
            return;
        }

        var legacyReservationId = await Db.Reservations
            .AsNoTracking()
            .Where(value => value.OrderId == integrationEvent.OrderId)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (legacyReservationId.HasValue)
        {
            var decision = InventoryRequestDecision.Accept(
                integrationEvent.OrderId,
                requestHash,
                legacyReservationId.Value,
                clock.GetUtcNow());
            Db.RequestDecisions.Add(decision);
            ReplayDecision(integrationEvent, decision);
            return;
        }

        var attempt = await ReserveAsync(integrationEvent, cancellationToken);
        var decidedOnUtc = clock.GetUtcNow();
        if (attempt.Reservation is not null)
        {
            Db.RequestDecisions.Add(InventoryRequestDecision.Accept(
                integrationEvent.OrderId,
                requestHash,
                attempt.Reservation.Id,
                decidedOnUtc));
            PublishReserved(integrationEvent, attempt.Reservation.Id);
            return;
        }

        var reason = attempt.RejectionReason
            ?? throw new InvalidOperationException("A rejected inventory attempt requires a reason.");
        Db.RequestDecisions.Add(InventoryRequestDecision.Reject(
            integrationEvent.OrderId,
            requestHash,
            reason,
            decidedOnUtc));
        Publish(new InventoryRejectedIntegrationEvent
        {
            OrderId = integrationEvent.OrderId,
            Reason = reason
        });
    }

    private async Task<ReservationAttempt> ReserveAsync(
        OrderSubmittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.Items.Count == 0)
            return ReservationAttempt.Rejected("An inventory request requires at least one item.");

        var requested = integrationEvent.Items
            .GroupBy(item => new
            {
                Sku = StockItem.NormalizeSku(item.Sku),
                OfferId = item.OfferId ?? integrationEvent.FlashSaleCampaignId
            })
            .Select(group => new RequestedLine(
                group.Key.Sku,
                checked(group.Sum(item => item.Quantity)),
                group.Key.OfferId))
            .OrderBy(line => line.Sku, StringComparer.Ordinal)
            .ThenBy(line => line.OfferId)
            .ToArray();
        if (requested.Any(line => line.Quantity <= 0))
            return ReservationAttempt.Rejected("Inventory quantities must be positive.");

        var regular = requested.Where(line => !line.OfferId.HasValue).ToArray();
        var offered = requested.Where(line => line.OfferId.HasValue).ToArray();
        var regularSkus = regular.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(regularSkus, cancellationToken);
        var stock = regularSkus.Length == 0
            ? new Dictionary<string, StockItem>(StringComparer.Ordinal)
            : await Db.StockItems
                .Where(value => regularSkus.Contains(value.Sku))
                .ToDictionaryAsync(value => value.Sku, StringComparer.Ordinal, cancellationToken);
        if (regular.Any(line => !stock.TryGetValue(line.Sku, out var item) || item.Available < line.Quantity))
            return ReservationAttempt.Rejected("Insufficient inventory.");

        var allocations = new List<(RequestedLine Line, InventoryAllocation Allocation, InventoryAllocationBuyer? Buyer)>();
        foreach (var line in offered)
        {
            var offerId = line.OfferId!.Value;
            var allocationId = await Db.InventoryAllocations
                .AsNoTracking()
                .Where(value => value.OfferId == offerId && value.Sku == line.Sku)
                .Select(value => (Guid?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (!allocationId.HasValue)
                return ReservationAttempt.Rejected($"Offer inventory was not found for SKU '{line.Sku}'.");

            await transactionLock.AcquireAllocationBuyerAsync(
                allocationId.Value,
                integrationEvent.UserId,
                cancellationToken);
            var allocation = await InventoryRowLocks.LockAllocationAsync(Db, allocationId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Inventory allocation disappeared while reserving stock.");
            var buyer = await Db.InventoryAllocationBuyers.SingleOrDefaultAsync(value =>
                value.AllocationId == allocation.Id && value.UserId == integrationEvent.UserId,
                cancellationToken);
            var buyerUsed = buyer is null ? 0 : buyer.ReservedQuantity + buyer.SoldQuantity;
            var now = clock.GetUtcNow();
            if (!allocation.AcceptsOrders(now))
                return ReservationAttempt.Rejected($"Offer is not accepting orders for SKU '{line.Sku}'.");
            if (allocation.AvailableQuantity < line.Quantity)
                return ReservationAttempt.Rejected($"Offer inventory is sold out for SKU '{line.Sku}'.");
            if ((long)buyerUsed + line.Quantity > allocation.PerUserLimit)
                return ReservationAttempt.Rejected($"Offer purchase limit is exceeded for SKU '{line.Sku}'.");
            allocations.Add((line, allocation, buyer));
        }

        var reservationId = Guid.NewGuid();
        var occurredOnUtc = clock.GetUtcNow();
        foreach (var line in regular)
        {
            stock[line.Sku].Reserve(line.Quantity);
            Db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryReserved,
                0,
                line.Quantity,
                StockMovementReferenceType.Reservation,
                reservationId,
                "Inventory reserved for an order.",
                occurredOnUtc));
        }

        foreach (var (line, allocation, existingBuyer) in allocations)
        {
            if (!allocation.TryReserve(line.Quantity, occurredOnUtc))
                throw new InvalidOperationException("Validated offer allocation could not be reserved.");
            if (existingBuyer is null)
            {
                Db.InventoryAllocationBuyers.Add(new InventoryAllocationBuyer(
                    allocation.Id,
                    integrationEvent.UserId,
                    line.Quantity,
                    allocation.PerUserLimit,
                    occurredOnUtc));
            }
            else
            {
                existingBuyer.Reserve(line.Quantity, allocation.PerUserLimit, occurredOnUtc);
            }
        }

        var lifetime = offered.Length == 0
            ? reservationOptions.Value.RegularLifetime
            : reservationOptions.Value.OfferLifetime;
        var reservation = new InventoryReservation(
            reservationId,
            integrationEvent.OrderId,
            integrationEvent.UserId,
            requested.Select(line =>
            {
                var allocation = allocations.SingleOrDefault(value => value.Line == line).Allocation;
                return new InventoryReservationLine(
                    line.Sku,
                    line.Quantity,
                    allocation?.Id,
                    line.OfferId);
            }),
            occurredOnUtc,
            ResolveExpiry(integrationEvent.PaymentDueOnUtc, occurredOnUtc, lifetime));
        Db.Reservations.Add(reservation);

        foreach (var line in requested)
            Cache.StockChanged(line.Sku);
        foreach (var (_, allocation, _) in allocations)
            Cache.AllocationChanged(allocation.Id);
        return ReservationAttempt.Accepted(reservation);
    }

    private void ReplayDecision(OrderSubmittedIntegrationEvent source, InventoryRequestDecision decision)
    {
        if (decision.IsAccepted)
        {
            PublishReserved(
                source,
                decision.ReservationId
                ?? throw new InvalidOperationException("Accepted inventory decision is missing its reservation id."));
            return;
        }
        Publish(new InventoryRejectedIntegrationEvent
        {
            OrderId = source.OrderId,
            Reason = decision.RejectionReason
                ?? throw new InvalidOperationException("Rejected inventory decision is missing its reason.")
        });
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

    private static DateTimeOffset ResolveExpiry(
        DateTimeOffset? requestedExpiry,
        DateTimeOffset now,
        TimeSpan fallbackLifetime) =>
        requestedExpiry.HasValue && requestedExpiry.Value > now
            ? requestedExpiry.Value
            : now.Add(fallbackLifetime);

    private sealed record RequestedLine(string Sku, int Quantity, Guid? OfferId);

    private sealed record ReservationAttempt(InventoryReservation? Reservation, string? RejectionReason)
    {
        public static ReservationAttempt Accepted(InventoryReservation reservation) => new(reservation, null);

        public static ReservationAttempt Rejected(string reason) => new(null, reason);
    }
}

internal sealed class ReleaseInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock)
    : InventoryIntegrationEventHandler<ReleaseInventoryIntegrationEvent>(db, outbox, cache, "inventory.release-requested.v2")
{
    protected override async Task HandleCoreAsync(
        ReleaseInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var reservation = await InventoryRowLocks.LockReservationAsync(
            Db, integrationEvent.ReservationId, integrationEvent.OrderId, cancellationToken);
        switch (reservation.Status)
        {
            case ReservationStatus.Reserved:
                var now = clock.GetUtcNow();
                await ApplyReleaseAsync(reservation, integrationEvent.Reason, now, cancellationToken);
                reservation.TryRelease(now, integrationEvent.Reason);
                break;
            case ReservationStatus.Released:
                // 重复释放请求只补发确认，不再改变余额。
                break;
            default:
                throw new InvalidOperationException("Committed inventory cannot be released.");
        }

        Publish(new InventoryReleasedIntegrationEvent
        {
            OrderId = integrationEvent.OrderId,
            ReservationId = integrationEvent.ReservationId
        });
    }

    private Task ApplyReleaseAsync(
        InventoryReservation reservation,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        InventoryRelease.ApplyAsync(Db, transactionLock, Cache, reservation, reason, now, cancellationToken);
}

/// <summary>
/// 归还预占占用的余额。释放消息与预占过期清理共用同一套逻辑，避免两条路径对配额与库存的处理产生分歧。
/// </summary>
internal static class InventoryRelease
{
    public static async Task ApplyAsync(
        InventoryDbContext db,
        IInventoryTransactionLock transactionLock,
        InventoryCacheInvalidator cache,
        InventoryReservation reservation,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (stock, allocations) = await InventoryReservationBalances.LockAsync(
            db, transactionLock, cache, reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            if (line.AllocationId is Guid allocationId)
            {
                var allocation = allocations[allocationId];
                allocation.Release(line.Quantity);
                var buyer = await InventoryRowLocks.LockBuyerAsync(
                    db, allocationId, InventoryReservationBalances.RequireUserId(reservation), cancellationToken);
                buyer.Release(line.Quantity, now);
                // 配额仍开放时，释放的数量回到配额池；配额关闭后才归还普通库存。
                if (allocation.Status != InventoryAllocationStatus.Closed)
                    continue;
            }

            stock[line.Sku].Release(line.Quantity);
            db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryReleased,
                0,
                -line.Quantity,
                StockMovementReferenceType.Reservation,
                reservation.Id,
                reason,
                now));
        }
    }
}

internal sealed class CommitInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock)
    : InventoryIntegrationEventHandler<CommitInventoryIntegrationEvent>(db, outbox, cache, "inventory.commit-requested.v2")
{
    protected override async Task HandleCoreAsync(
        CommitInventoryIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var reservation = await InventoryRowLocks.LockReservationAsync(
            Db, integrationEvent.ReservationId, integrationEvent.OrderId, cancellationToken);
        switch (reservation.Status)
        {
            case ReservationStatus.Reserved:
                var now = clock.GetUtcNow();
                await ApplyCommitAsync(reservation, now, cancellationToken);
                reservation.TryCommit(now);
                break;
            case ReservationStatus.Committed:
                break;
            default:
                throw new InvalidOperationException("Released inventory cannot be committed.");
        }

        Publish(new InventoryCommittedIntegrationEvent
        {
            OrderId = integrationEvent.OrderId,
            ReservationId = integrationEvent.ReservationId
        });
    }

    private async Task ApplyCommitAsync(
        InventoryReservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (stock, allocations) = await InventoryReservationBalances.LockAsync(
            Db, transactionLock, Cache, reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            if (line.AllocationId is Guid allocationId)
            {
                allocations[allocationId].Commit(line.Quantity);
                var buyer = await InventoryRowLocks.LockBuyerAsync(
                    Db, allocationId, InventoryReservationBalances.RequireUserId(reservation), cancellationToken);
                buyer.Commit(line.Quantity, now);
            }

            stock[line.Sku].Commit(line.Quantity);
            Db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryCommitted,
                -line.Quantity,
                -line.Quantity,
                StockMovementReferenceType.Reservation,
                reservation.Id,
                "Reserved inventory committed after payment.",
                now));
        }
    }
}

/// <summary>Locks every balance a reservation touches, in the same order for release and commit.</summary>
internal static class InventoryReservationBalances
{
    public static async Task<(Dictionary<string, StockItem> Stock, Dictionary<Guid, InventoryAllocation> Allocations)> LockAsync(
        InventoryDbContext db,
        IInventoryTransactionLock transactionLock,
        InventoryCacheInvalidator cache,
        InventoryReservation reservation,
        CancellationToken cancellationToken)
    {
        var skus = reservation.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var stock = await db.StockItems
            .Where(value => skus.Contains(value.Sku))
            .ToDictionaryAsync(value => value.Sku, StringComparer.Ordinal, cancellationToken);
        var allocations = await InventoryRowLocks.LockAllocationsAsync(
            db,
            reservation.Lines.Where(line => line.AllocationId.HasValue).Select(line => line.AllocationId!.Value),
            cancellationToken);

        foreach (var sku in skus)
            cache.StockChanged(sku);
        foreach (var allocationId in allocations.Keys)
            cache.AllocationChanged(allocationId);
        return (stock, allocations);
    }

    public static string RequireUserId(InventoryReservation reservation) =>
        reservation.UserId
        ?? throw new InvalidOperationException("An allocated reservation is missing its user id.");
}
