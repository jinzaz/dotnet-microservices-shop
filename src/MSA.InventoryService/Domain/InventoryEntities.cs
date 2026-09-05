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
        Sku = sku.Trim();
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
        OnHand = checked(OnHand + quantity);
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

    public InventoryReservation(Guid id, Guid orderId, IEnumerable<InventoryReservationLine> lines)
        : this(id, orderId, lines, DateTimeOffset.UtcNow)
    {
    }

    public InventoryReservation(
        Guid id,
        Guid orderId,
        IEnumerable<InventoryReservationLine> lines,
        DateTimeOffset createdOnUtc)
    {
        var reservationLines = lines.ToArray();
        if (reservationLines.Length == 0)
            throw new ArgumentException("An inventory reservation requires at least one line.", nameof(lines));

        Id = id;
        OrderId = orderId;
        Status = ReservationStatus.Reserved;
        CreatedOnUtc = createdOnUtc;
        _lines.AddRange(reservationLines);
    }

    private InventoryReservation(
        Guid id,
        Guid orderId,
        string userId,
        Guid campaignId,
        long stockBucketId,
        InventoryReservationLine line,
        DateTimeOffset createdOnUtc)
        : this(id, orderId, [line], createdOnUtc)
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
        new(id, orderId, userId, campaignId, stockBucketId, new InventoryReservationLine(sku, quantity), createdOnUtc);

    public bool TryRelease()
    {
        if (Status != ReservationStatus.Reserved)
            return false;

        Status = ReservationStatus.Released;
        Version++;
        return true;
    }

    public bool TryCommit()
    {
        if (Status != ReservationStatus.Reserved)
            return false;

        Status = ReservationStatus.Committed;
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
        Sku = sku.Trim();
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
