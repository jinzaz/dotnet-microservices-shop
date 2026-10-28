using MSA.Infrastrucute.Entities;

namespace MSA.InventoryService.Domain;

public sealed class StockItem : AggregateRoot<Guid>
{
    private StockItem()
    {
    }

    public StockItem(Guid id, string sku, int onHand)
    {
        if (onHand < 0)
            throw new ArgumentOutOfRangeException(nameof(onHand));
        Id = id;
        Sku = NormalizeSku(sku);
        OnHand = onHand;
    }

    /// <summary>Canonical SKU key shared by stock rows, reservation lines, allocations, locks and cache keys.</summary>
    public static string NormalizeSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return sku.Trim().ToUpperInvariant();
    }

    public string Sku { get; private set; } = string.Empty;
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => OnHand - Reserved;
    public long Version { get; private set; }

    public void Increase(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Adjust(quantity);
    }

    public void Adjust(int quantityDelta)
    {
        if (quantityDelta == 0)
            throw new ArgumentOutOfRangeException(nameof(quantityDelta));
        var adjustedOnHand = (long)OnHand + quantityDelta;
        if (adjustedOnHand < Reserved || adjustedOnHand > int.MaxValue)
            throw new InvalidOperationException($"Inventory adjustment for {Sku} would produce an invalid on-hand balance.");
        OnHand = (int)adjustedOnHand;
        Version++;
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0 || Available < quantity)
            throw new InvalidOperationException($"Insufficient inventory for {Sku}.");
        Reserved = checked(Reserved + quantity);
        Version++;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0 || Reserved < quantity)
            throw new InvalidOperationException($"Invalid release for {Sku}.");
        Reserved -= quantity;
        Version++;
    }

    public void Commit(int quantity)
    {
        if (quantity <= 0 || Reserved < quantity || OnHand < quantity)
            throw new InvalidOperationException($"Invalid commit for {Sku}.");
        Reserved -= quantity;
        OnHand -= quantity;
        Version++;
    }
}

public sealed class InventoryReservation : AggregateRoot<Guid>
{
    private readonly List<InventoryReservationLine> _lines = [];

    private InventoryReservation()
    {
    }

    public InventoryReservation(
        Guid id,
        Guid orderId,
        IEnumerable<InventoryReservationLine> lines,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc)
        : this(id, orderId, null, lines, createdOnUtc, expiresOnUtc)
    {
    }

    public InventoryReservation(
        Guid id,
        Guid orderId,
        string? userId,
        IEnumerable<InventoryReservationLine> lines,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc)
    {
        var reservationLines = lines.ToArray();
        if (reservationLines.Length == 0)
            throw new ArgumentException("An inventory reservation requires at least one line.", nameof(lines));
        if (id == Guid.Empty)
            throw new ArgumentException("Reservation id is required.", nameof(id));
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        if (expiresOnUtc <= createdOnUtc)
            throw new ArgumentException("Reservation expiry must be after its creation time.", nameof(expiresOnUtc));

        Id = id;
        OrderId = orderId;
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        Status = ReservationStatus.Reserved;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        _lines.AddRange(reservationLines);
    }

    public Guid OrderId { get; private set; }
    public string? UserId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset ExpiresOnUtc { get; private set; }
    public DateTimeOffset? CommittedOnUtc { get; private set; }
    public DateTimeOffset? ReleasedOnUtc { get; private set; }
    public string? TerminalReason { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<InventoryReservationLine> Lines => _lines;
    public bool HasAllocations => _lines.Any(line => line.AllocationId.HasValue);

    public bool TryRelease(DateTimeOffset releasedOnUtc, string reason)
    {
        if (Status != ReservationStatus.Reserved)
            return false;
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 1000)
            throw new ArgumentException("Release reason cannot exceed 1000 characters.", nameof(reason));
        if (releasedOnUtc < CreatedOnUtc)
            throw new ArgumentOutOfRangeException(nameof(releasedOnUtc));

        Status = ReservationStatus.Released;
        UpdatedOnUtc = releasedOnUtc;
        ReleasedOnUtc = releasedOnUtc;
        TerminalReason = normalizedReason;
        Version++;
        return true;
    }

    public bool TryCommit(DateTimeOffset committedOnUtc)
    {
        if (Status != ReservationStatus.Reserved)
            return false;
        if (committedOnUtc < CreatedOnUtc)
            throw new ArgumentOutOfRangeException(nameof(committedOnUtc));
        Status = ReservationStatus.Committed;
        UpdatedOnUtc = committedOnUtc;
        CommittedOnUtc = committedOnUtc;
        Version++;
        return true;
    }

    /// <summary>
    /// 预占已被释放、订单却已付款时补扣成功后调用。已提交状态不带释放时间和原因（见 CK 约束），
    /// 释放与补扣的经过保留在库存流水里。
    /// </summary>
    public bool TryCommitAfterRelease(DateTimeOffset committedOnUtc)
    {
        if (Status != ReservationStatus.Released)
            return false;
        if (committedOnUtc < CreatedOnUtc)
            throw new ArgumentOutOfRangeException(nameof(committedOnUtc));
        Status = ReservationStatus.Committed;
        UpdatedOnUtc = committedOnUtc;
        CommittedOnUtc = committedOnUtc;
        ReleasedOnUtc = null;
        TerminalReason = null;
        Version++;
        return true;
    }
}

public sealed class InventoryReservationLine
{
    private InventoryReservationLine()
    {
    }

    public InventoryReservationLine(string sku, int quantity)
        : this(sku, quantity, null, null)
    {
    }

    public InventoryReservationLine(
        string sku,
        int quantity,
        Guid? allocationId,
        Guid? offerId)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (allocationId.HasValue != offerId.HasValue)
            throw new ArgumentException("AllocationId and OfferId must be provided together.");
        if (allocationId == Guid.Empty || offerId == Guid.Empty)
            throw new ArgumentException("Allocation identifiers cannot be empty GUID values.");
        Sku = StockItem.NormalizeSku(sku);
        Quantity = quantity;
        AllocationId = allocationId;
        OfferId = offerId;
    }

    public long Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public Guid? AllocationId { get; private set; }
    public Guid? OfferId { get; private set; }
}

public enum ReservationStatus
{
    Reserved,
    Committed,
    Released
}
