using MSA.Infrastrucute.Entities;

namespace MSA.InventoryService.Domain;

public sealed class StockItem : AggregateRoot<Guid>
{
    private StockItem()
    {
    }

    public StockItem(Guid id, string sku, int onHand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        if (onHand < 0)
            throw new ArgumentOutOfRangeException(nameof(onHand));

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        OnHand = onHand;
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
            throw new InvalidOperationException(
                $"Inventory adjustment for {Sku} would produce an invalid on-hand balance.");

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
    private static readonly TimeSpan DefaultReservationLifetime = TimeSpan.FromMinutes(20);
    private readonly List<InventoryReservationLine> _lines = [];

    private InventoryReservation()
    {
    }

    public InventoryReservation(Guid id, Guid orderId, IEnumerable<InventoryReservationLine> lines)
        : this(id, orderId, lines, DateTimeOffset.UtcNow)
    {
    }

    public InventoryReservation(
        Guid id,
        Guid orderId,
        IEnumerable<InventoryReservationLine> lines,
        DateTimeOffset createdOnUtc)
        : this(id, orderId, lines, createdOnUtc, createdOnUtc.Add(DefaultReservationLifetime))
    {
    }

    public InventoryReservation(
        Guid id,
        Guid orderId,
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
        Status = ReservationStatus.Reserved;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        _lines.AddRange(reservationLines);
    }

    private InventoryReservation(
        Guid id,
        Guid orderId,
        string userId,
        Guid campaignId,
        long stockBucketId,
        InventoryReservationLine line,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc)
        : this(id, orderId, [line], createdOnUtc, expiresOnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (campaignId == Guid.Empty)
            throw new ArgumentException("Campaign id is required.", nameof(campaignId));
        if (stockBucketId <= 0)
            throw new ArgumentOutOfRangeException(nameof(stockBucketId));

        UserId = userId;
        FlashSaleCampaignId = campaignId;
        FlashSaleStockBucketId = stockBucketId;
    }

    public Guid OrderId { get; private set; }
    public string? UserId { get; private set; }
    public Guid? FlashSaleCampaignId { get; private set; }
    public long? FlashSaleStockBucketId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset ExpiresOnUtc { get; private set; }
    public DateTimeOffset? CommittedOnUtc { get; private set; }
    public DateTimeOffset? ReleasedOnUtc { get; private set; }
    public string? TerminalReason { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<InventoryReservationLine> Lines => _lines;
    public bool IsFlashSale => FlashSaleCampaignId.HasValue;

    public static InventoryReservation CreateFlashSale(
        Guid id,
        Guid orderId,
        string userId,
        Guid campaignId,
        long stockBucketId,
        string sku,
        int quantity,
        DateTimeOffset createdOnUtc) =>
        CreateFlashSale(
            id,
            orderId,
            userId,
            campaignId,
            stockBucketId,
            sku,
            quantity,
            createdOnUtc,
            createdOnUtc.Add(DefaultReservationLifetime));

    public static InventoryReservation CreateFlashSale(
        Guid id,
        Guid orderId,
        string userId,
        Guid campaignId,
        long stockBucketId,
        string sku,
        int quantity,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc) =>
        new(
            id,
            orderId,
            userId,
            campaignId,
            stockBucketId,
            new InventoryReservationLine(sku, quantity),
            createdOnUtc,
            expiresOnUtc);

    public bool TryRelease()
        => TryRelease(DateTimeOffset.UtcNow, "Inventory reservation released.");

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

    public bool TryCommit()
        => TryCommit(DateTimeOffset.UtcNow);

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
}

public sealed class InventoryReservationLine
{
    private InventoryReservationLine()
    {
    }

    public InventoryReservationLine(string sku, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Sku = sku.Trim().ToUpperInvariant();
        Quantity = quantity;
    }

    public long Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
}

public enum ReservationStatus
{
    Reserved,
    Committed,
    Released
}
