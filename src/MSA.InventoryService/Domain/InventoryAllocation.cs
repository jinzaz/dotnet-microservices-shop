using MSA.Infrastrucute.Entities;

namespace MSA.InventoryService.Domain;

/// <summary>
/// Durable stock ring-fence for any limited offer. PostgreSQL is the source of truth; one row is
/// intentionally used until measured contention justifies partitioning.
/// </summary>
public sealed class InventoryAllocation : AggregateRoot<Guid>
{
    private InventoryAllocation()
    {
    }

    public InventoryAllocation(
        Guid id,
        Guid offerId,
        string sku,
        int totalQuantity,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int perUserLimit,
        DateTimeOffset createdOnUtc)
    {
        if (id == Guid.Empty || offerId == Guid.Empty)
            throw new ArgumentException("Allocation and offer ids are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        if (totalQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalQuantity));
        if (startsOnUtc >= endsOnUtc)
            throw new ArgumentException("Allocation end must be after its start.", nameof(endsOnUtc));
        if (perUserLimit <= 0 || perUserLimit > totalQuantity)
            throw new ArgumentOutOfRangeException(nameof(perUserLimit));

        Id = id;
        OfferId = offerId;
        Sku = sku.Trim().ToUpperInvariant();
        TotalQuantity = totalQuantity;
        StartsOnUtc = startsOnUtc.ToUniversalTime();
        EndsOnUtc = endsOnUtc.ToUniversalTime();
        PerUserLimit = perUserLimit;
        Status = InventoryAllocationStatus.Open;
        CreatedOnUtc = createdOnUtc.ToUniversalTime();
    }

    public Guid OfferId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int TotalQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int SoldQuantity { get; private set; }
    public int ReleasedQuantity { get; private set; }
    public int AvailableQuantity => TotalQuantity - ReservedQuantity - SoldQuantity - ReleasedQuantity;
    public int PerUserLimit { get; private set; }
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public InventoryAllocationStatus Status { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public long Version { get; private set; }

    public bool AcceptsOrders(DateTimeOffset now) =>
        Status == InventoryAllocationStatus.Open
        && StartsOnUtc <= now
        && now < EndsOnUtc;

    public bool TryReserve(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!AcceptsOrders(now) || AvailableQuantity < quantity)
            return false;
        ReservedQuantity = checked(ReservedQuantity + quantity);
        Version++;
        return true;
    }

    public void Commit(int quantity)
    {
        EnsureReserved(quantity);
        ReservedQuantity -= quantity;
        SoldQuantity = checked(SoldQuantity + quantity);
        Version++;
    }

    public void Release(int quantity)
    {
        EnsureReserved(quantity);
        ReservedQuantity -= quantity;
        if (Status == InventoryAllocationStatus.Closed)
            ReleasedQuantity = checked(ReleasedQuantity + quantity);
        Version++;
    }

    public int Close(DateTimeOffset now)
    {
        if (Status == InventoryAllocationStatus.Closed)
            return 0;
        var released = AvailableQuantity;
        ReleasedQuantity = checked(ReleasedQuantity + released);
        Status = InventoryAllocationStatus.Closed;
        ClosedOnUtc = now.ToUniversalTime();
        Version++;
        return released;
    }

    private void EnsureReserved(int quantity)
    {
        if (quantity <= 0 || ReservedQuantity < quantity)
            throw new InvalidOperationException("Allocation does not contain the requested reserved quantity.");
    }
}

public sealed class InventoryAllocationBuyer
{
    private InventoryAllocationBuyer()
    {
    }

    public InventoryAllocationBuyer(
        Guid allocationId,
        string userId,
        int quantity,
        int perUserLimit,
        DateTimeOffset createdOnUtc)
    {
        if (allocationId == Guid.Empty)
            throw new ArgumentException("Allocation id is required.", nameof(allocationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        AllocationId = allocationId;
        UserId = userId.Trim();
        CreatedOnUtc = createdOnUtc.ToUniversalTime();
        UpdatedOnUtc = CreatedOnUtc;
        Reserve(quantity, perUserLimit, CreatedOnUtc);
    }

    public Guid AllocationId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public int ReservedQuantity { get; private set; }
    public int SoldQuantity { get; private set; }
    public int ReleasedQuantity { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public long Version { get; private set; }

    public void Reserve(int quantity, int perUserLimit, DateTimeOffset now)
    {
        if (quantity <= 0 || perUserLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if ((long)ReservedQuantity + SoldQuantity + quantity > perUserLimit)
            throw new InvalidOperationException("The offer purchase limit for this buyer has been exceeded.");
        ReservedQuantity = checked(ReservedQuantity + quantity);
        UpdatedOnUtc = now.ToUniversalTime();
        Version++;
    }

    public void Commit(int quantity, DateTimeOffset now)
    {
        EnsureReserved(quantity);
        ReservedQuantity -= quantity;
        SoldQuantity = checked(SoldQuantity + quantity);
        UpdatedOnUtc = now.ToUniversalTime();
        Version++;
    }

    public void Release(int quantity, DateTimeOffset now)
    {
        EnsureReserved(quantity);
        ReservedQuantity -= quantity;
        ReleasedQuantity = checked(ReleasedQuantity + quantity);
        UpdatedOnUtc = now.ToUniversalTime();
        Version++;
    }

    private void EnsureReserved(int quantity)
    {
        if (quantity <= 0 || ReservedQuantity < quantity)
            throw new InvalidOperationException("Buyer allocation does not contain the requested reservation.");
    }
}

public enum InventoryAllocationStatus
{
    Open = 0,
    Closed = 1
}
