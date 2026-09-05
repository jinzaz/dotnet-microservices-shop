using MSA.Infrastrucute.Entities;

namespace MSA.OrderService.Domain;

public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Order(
        Guid id,
        string userId,
        string idempotencyKey,
        IEnumerable<OrderLine> lines,
        string currency,
        Guid? flashSaleCampaignId = null,
        DateTimeOffset? createdOnUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var orderLines = lines.ToArray();
        if (orderLines.Length == 0)
            throw new ArgumentException("Order requires items.", nameof(lines));
        if (flashSaleCampaignId.HasValue && orderLines.Length != 1)
            throw new ArgumentException("A flash-sale order requires exactly one item.", nameof(lines));

        Id = id;
        UserId = userId;
        IdempotencyKey = idempotencyKey;
        Currency = currency;
        FlashSaleCampaignId = flashSaleCampaignId;
        _lines.AddRange(orderLines);
        Amount = _lines.Sum(line => line.UnitPrice * line.Quantity);
        Status = OrderStatus.Submitted;
        CreatedOnUtc = createdOnUtc ?? DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? FlashSaleCampaignId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public OrderStatus Status { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public void InventoryReserved(Guid reservationId)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Order is not awaiting inventory.");
        ReservationId = reservationId;
        Status = OrderStatus.AwaitingPayment;
        Version++;
    }

    public void Confirm(Guid paymentId)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Order is not awaiting payment.");
        PaymentId = paymentId;
        Status = OrderStatus.Confirmed;
        Version++;
    }

    public void Cancel(string reason)
    {
        TryCancel(reason);
    }

    public bool TryCancel(string reason)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
            return false;
        CancellationReason = reason;
        Status = OrderStatus.Cancelled;
        Version++;
        return true;
    }
}

public sealed class OrderLine
{
    private OrderLine()
    {
    }

    public OrderLine(string sku, string productName, int quantity, decimal unitPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Sku = sku;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public long Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
}

public sealed class OrderProductProjection
{
    private OrderProductProjection()
    {
    }

    public OrderProductProjection(Guid productId, string sku, string name, decimal price, string currency, bool active)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        Price = price;
        Currency = currency;
        IsActive = active;
        UpdatedOnUtc = DateTimeOffset.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    public void Update(string name, decimal price, string currency, bool active)
    {
        Name = name;
        Price = price;
        Currency = currency;
        IsActive = active;
        UpdatedOnUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class FlashSaleCampaignProjection
{
    private FlashSaleCampaignProjection()
    {
    }

    public FlashSaleCampaignProjection(
        Guid campaignId,
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        string strategy,
        DateTimeOffset updatedOnUtc)
    {
        CampaignId = campaignId;
        ApplyOpen(sku, startsOnUtc, endsOnUtc, purchaseLimit, strategy, updatedOnUtc);
    }

    public Guid CampaignId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int PurchaseLimit { get; private set; }
    public string Strategy { get; private set; } = string.Empty;
    public bool IsOpen { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public long Version { get; private set; }

    public bool AcceptsOrders(DateTimeOffset now) => IsOpen && StartsOnUtc <= now && now < EndsOnUtc;

    public void ApplyOpen(
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        string strategy,
        DateTimeOffset updatedOnUtc)
    {
        if (ClosedOnUtc.HasValue && ClosedOnUtc.Value >= updatedOnUtc)
            return;

        Sku = sku;
        StartsOnUtc = startsOnUtc;
        EndsOnUtc = endsOnUtc;
        PurchaseLimit = purchaseLimit;
        Strategy = strategy;
        IsOpen = true;
        ClosedOnUtc = null;
        UpdatedOnUtc = updatedOnUtc;
        Version++;
    }

    public void Close(DateTimeOffset closedOnUtc)
    {
        if (ClosedOnUtc.HasValue && ClosedOnUtc.Value >= closedOnUtc)
            return;

        IsOpen = false;
        ClosedOnUtc = closedOnUtc;
        UpdatedOnUtc = closedOnUtc;
        Version++;
    }
}

public enum OrderStatus
{
    Submitted,
    AwaitingPayment,
    Confirmed,
    Cancelled
}
