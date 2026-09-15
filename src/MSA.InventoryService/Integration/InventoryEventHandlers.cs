using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Integration;

internal sealed class OrderSubmittedHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    IOptions<InventoryReservationOptions> reservationOptions,
    TimeProvider clock) : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
{
    private const string ConsumerId = "inventory.order-submitted.v2";

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

        string[] changedSkus = [];
        Guid[] changedAllocationIds = [];
        try
        {
            await transactionLock.AcquireOrderReservationAsync(integrationEvent.OrderId, cancellationToken);
            var existingDecision = await db.RequestDecisions
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == integrationEvent.OrderId, cancellationToken);
            if (existingDecision is not null)
            {
                existingDecision.EnsureMatches(requestHash);
                ReplayDecision(integrationEvent, existingDecision);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            var legacyReservationId = await db.Reservations
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
                db.RequestDecisions.Add(decision);
                ReplayDecision(integrationEvent, decision);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            var attempt = await ReserveAsync(integrationEvent, cancellationToken);
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
                var reason = attempt.RejectionReason
                    ?? throw new InvalidOperationException("A rejected inventory attempt requires a reason.");
                db.RequestDecisions.Add(InventoryRequestDecision.Reject(
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

            changedSkus = attempt.ChangedSkus;
            changedAllocationIds = attempt.ChangedAllocationIds;
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await InvalidateAsync(changedSkus, changedAllocationIds, cancellationToken);
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
                Sku = item.Sku.Trim().ToUpperInvariant(),
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
            : await db.StockItems
                .Where(value => regularSkus.Contains(value.Sku))
                .ToDictionaryAsync(value => value.Sku, StringComparer.Ordinal, cancellationToken);
        if (regular.Any(line => !stock.TryGetValue(line.Sku, out var item) || item.Available < line.Quantity))
            return ReservationAttempt.Rejected("Insufficient inventory.");

        var allocations = new List<(RequestedLine Line, InventoryAllocation Allocation, InventoryAllocationBuyer? Buyer)>();
        foreach (var line in offered)
        {
            var offerId = line.OfferId!.Value;
            var allocationId = await db.InventoryAllocations
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
            var allocation = await InventoryRowLocks.LockAllocationAsync(db, allocationId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Inventory allocation disappeared while reserving stock.");
            var buyer = await db.InventoryAllocationBuyers.SingleOrDefaultAsync(value =>
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
            db.StockMovements.Add(StockMovement.Record(
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
                db.InventoryAllocationBuyers.Add(new InventoryAllocationBuyer(
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
        db.Reservations.Add(reservation);
        return ReservationAttempt.Accepted(
            reservation,
            requested.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray(),
            allocations.Select(value => value.Allocation.Id).Distinct().ToArray());
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

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

    private async Task InvalidateAsync(
        IEnumerable<string> skus,
        IEnumerable<Guid> allocationIds,
        CancellationToken cancellationToken)
    {
        foreach (var sku in skus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        foreach (var id in allocationIds)
            await cache.RemoveAllocationAsync(id, cancellationToken);
    }

    private static DateTimeOffset ResolveExpiry(
        DateTimeOffset? requestedExpiry,
        DateTimeOffset now,
        TimeSpan fallbackLifetime) =>
        requestedExpiry.HasValue && requestedExpiry.Value > now
            ? requestedExpiry.Value
            : now.Add(fallbackLifetime);

    private sealed record RequestedLine(string Sku, int Quantity, Guid? OfferId);

    private sealed record ReservationAttempt(
        InventoryReservation? Reservation,
        string? RejectionReason,
        string[] ChangedSkus,
        Guid[] ChangedAllocationIds)
    {
        public static ReservationAttempt Accepted(
            InventoryReservation reservation,
            string[] changedSkus,
            Guid[] changedAllocationIds) =>
            new(reservation, null, changedSkus, changedAllocationIds);

        public static ReservationAttempt Rejected(string reason) =>
            new(null, reason, [], []);
    }
}

internal sealed class ReleaseInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IIntegrationEventHandler<ReleaseInventoryIntegrationEvent>
{
    private const string ConsumerId = "inventory.release-requested.v2";

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

        string[] skus = [];
        Guid[] allocationIds = [];
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(
                db, integrationEvent.ReservationId, integrationEvent.OrderId, cancellationToken);
            if (reservation.Status == ReservationStatus.Reserved)
            {
                var now = clock.GetUtcNow();
                (skus, allocationIds) = await ApplyReleaseAsync(reservation, integrationEvent.Reason, now, cancellationToken);
                reservation.TryRelease(now, integrationEvent.Reason);
                Publish(new InventoryReleasedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
            }
            else if (reservation.Status == ReservationStatus.Released)
            {
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

        await InvalidateAsync(skus, allocationIds, cancellationToken);
    }

    private async Task<(string[] Skus, Guid[] AllocationIds)> ApplyReleaseAsync(
        InventoryReservation reservation,
        string reason,
        DateTimeOffset now,
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

        foreach (var line in reservation.Lines)
        {
            if (line.AllocationId is not Guid allocationId)
            {
                stock[line.Sku].Release(line.Quantity);
                AddMovement(stock[line.Sku], line.Quantity, reservation.Id, reason, now);
                continue;
            }

            var allocation = allocations[allocationId];
            allocation.Release(line.Quantity);
            var userId = reservation.UserId
                ?? throw new InvalidOperationException("An allocated reservation is missing its user id.");
            var buyer = await InventoryRowLocks.LockBuyerAsync(db, allocationId, userId, cancellationToken);
            buyer.Release(line.Quantity, now);
            if (allocation.Status == InventoryAllocationStatus.Closed)
            {
                stock[line.Sku].Release(line.Quantity);
                AddMovement(stock[line.Sku], line.Quantity, reservation.Id, reason, now);
            }
        }

        return (skus, allocations.Keys.ToArray());
    }

    private void AddMovement(
        StockItem stock,
        int quantity,
        Guid reservationId,
        string reason,
        DateTimeOffset now) =>
        db.StockMovements.Add(StockMovement.Record(
            stock,
            StockMovementType.InventoryReleased,
            0,
            -quantity,
            StockMovementReferenceType.Reservation,
            reservationId,
            reason,
            now));

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

    private async Task InvalidateAsync(string[] skus, Guid[] allocationIds, CancellationToken cancellationToken)
    {
        foreach (var sku in skus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        foreach (var id in allocationIds)
            await cache.RemoveAllocationAsync(id, cancellationToken);
    }
}

internal sealed class CommitInventoryHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IIntegrationEventHandler<CommitInventoryIntegrationEvent>
{
    private const string ConsumerId = "inventory.commit-requested.v2";

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

        string[] skus = [];
        Guid[] allocationIds = [];
        try
        {
            var reservation = await InventoryRowLocks.LockReservationAsync(
                db, integrationEvent.ReservationId, integrationEvent.OrderId, cancellationToken);
            if (reservation.Status == ReservationStatus.Reserved)
            {
                var now = clock.GetUtcNow();
                (skus, allocationIds) = await ApplyCommitAsync(reservation, now, cancellationToken);
                reservation.TryCommit(now);
                Publish(new InventoryCommittedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
            }
            else if (reservation.Status == ReservationStatus.Committed)
            {
                Publish(new InventoryCommittedIntegrationEvent
                {
                    OrderId = integrationEvent.OrderId,
                    ReservationId = integrationEvent.ReservationId
                });
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

        await InvalidateAsync(skus, allocationIds, cancellationToken);
    }

    private async Task<(string[] Skus, Guid[] AllocationIds)> ApplyCommitAsync(
        InventoryReservation reservation,
        DateTimeOffset now,
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

        foreach (var line in reservation.Lines)
        {
            if (line.AllocationId is Guid allocationId)
            {
                var allocation = allocations[allocationId];
                allocation.Commit(line.Quantity);
                var userId = reservation.UserId
                    ?? throw new InvalidOperationException("An allocated reservation is missing its user id.");
                var buyer = await InventoryRowLocks.LockBuyerAsync(db, allocationId, userId, cancellationToken);
                buyer.Commit(line.Quantity, now);
            }

            stock[line.Sku].Commit(line.Quantity);
            db.StockMovements.Add(StockMovement.Record(
                stock[line.Sku],
                StockMovementType.InventoryCommitted,
                -line.Quantity,
                -line.Quantity,
                StockMovementReferenceType.Reservation,
                reservation.Id,
                "Reserved inventory committed after payment.",
                now));
        }

        return (skus, allocations.Keys.ToArray());
    }

    private void Publish(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

    private async Task InvalidateAsync(string[] skus, Guid[] allocationIds, CancellationToken cancellationToken)
    {
        foreach (var sku in skus)
            await cache.RemoveStockAsync(sku, cancellationToken);
        foreach (var id in allocationIds)
            await cache.RemoveAllocationAsync(id, cancellationToken);
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
            ?? throw new InvalidOperationException($"Inventory reservation {reservationId} was not found for order {orderId}.");
        await db.Entry(reservation).Collection(value => value.Lines).LoadAsync(cancellationToken);
        return reservation;
    }

    public static Task<InventoryAllocation?> LockAllocationAsync(
        InventoryDbContext db,
        Guid allocationId,
        CancellationToken cancellationToken) =>
        db.InventoryAllocations
            .FromSqlInterpolated($"SELECT * FROM inventory_allocations WHERE \"Id\" = {allocationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static async Task<Dictionary<Guid, InventoryAllocation>> LockAllocationsAsync(
        InventoryDbContext db,
        IEnumerable<Guid> allocationIds,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<Guid, InventoryAllocation>();
        foreach (var id in allocationIds.Distinct().Order())
        {
            values[id] = await LockAllocationAsync(db, id, cancellationToken)
                ?? throw new InvalidOperationException($"Inventory allocation {id} was not found.");
        }
        return values;
    }

    public static Task<InventoryAllocationBuyer> LockBuyerAsync(
        InventoryDbContext db,
        Guid allocationId,
        string userId,
        CancellationToken cancellationToken) =>
        db.InventoryAllocationBuyers
            .FromSqlInterpolated($"SELECT * FROM inventory_allocation_buyers WHERE \"AllocationId\" = {allocationId} AND \"UserId\" = {userId} FOR UPDATE")
            .SingleAsync(cancellationToken);
}
