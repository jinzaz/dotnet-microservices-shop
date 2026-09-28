using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
using MSA.InventoryService.Domain;

namespace MSA.InventoryService.Infrastructure;

/// <summary>Pessimistic row locks shared by admin commands and saga message handlers.</summary>
internal static class InventoryRowLocks
{
    public static async Task<InventoryReservation> LockReservationAsync(
        InventoryDbContext db,
        Guid reservationId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations
            .Where(value => value.Id == reservationId && value.OrderId == orderId)
            .ForUpdate()
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
            .Where(value => value.Id == allocationId)
            .ForUpdate()
            .SingleOrDefaultAsync(cancellationToken);

    public static async Task<Dictionary<Guid, InventoryAllocation>> LockAllocationsAsync(
        InventoryDbContext db,
        IEnumerable<Guid> allocationIds,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<Guid, InventoryAllocation>();
        // 固定加锁顺序，避免并发释放/提交在多个配额行上死锁。
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
            .Where(value => value.AllocationId == allocationId && value.UserId == userId)
            .ForUpdate()
            .SingleAsync(cancellationToken);
}
