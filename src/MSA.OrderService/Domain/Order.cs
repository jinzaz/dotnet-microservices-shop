using MSA.Infrastrucute.Entities;

namespace MSA.OrderService.Domain;

public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderDiscount> _discounts = [];
    private readonly List<OrderStatusHistory> _statusHistory = [];

    private Order()
    {
    }

    // Compatibility constructor for existing callers and stored orders.
    public Order(
        Guid id,
        string userId,
        string idempotencyKey,
        IEnumerable<OrderLine> lines,
        string currency,
        Guid? flashSaleCampaignId = null,
        DateTimeOffset? createdOnUtc = null)
        : this(
            id,
            userId,
            idempotencyKey,
            $"legacy:{id:N}",
            "Unknown",
            lines,
            [],
            currency,
            flashSaleCampaignId,
            Guid.NewGuid(),
            $"legacy:{id:N}",
            (createdOnUtc ?? DateTimeOffset.UtcNow).AddMinutes(15),
            createdOnUtc ?? DateTimeOffset.UtcNow)
    {
    }

    public Order(
        Guid id,
        string userId,
        string idempotencyKey,
        string requestFingerprint,
        string paymentMethod,
        IEnumerable<OrderLine> lines,
        IEnumerable<OrderDiscount> discounts,
        string currency,
        Guid? flashSaleCampaignId,
        Guid pricingSnapshotId,
        string pricingSnapshotHash,
        DateTimeOffset paymentDueOnUtc,
        DateTimeOffset createdOnUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(pricingSnapshotHash);
        if (pricingSnapshotId == Guid.Empty)
            throw new ArgumentException("Pricing snapshot id is required.", nameof(pricingSnapshotId));
        if (paymentDueOnUtc <= createdOnUtc)
            throw new ArgumentOutOfRangeException(nameof(paymentDueOnUtc));

        var orderLines = lines.ToArray();
        if (orderLines.Length == 0)
            throw new ArgumentException("Order requires items.", nameof(lines));
        if (flashSaleCampaignId.HasValue && orderLines.Length != 1)
            throw new ArgumentException("A flash-sale order requires exactly one item.", nameof(lines));

        var orderDiscounts = discounts.ToArray();
        var subtotal = Money.Round(orderLines.Sum(line => line.LineSubtotal));
        var discount = Money.Round(orderDiscounts.Sum(item => item.Amount));
        if (subtotal <= 0)
            throw new InvalidOperationException("Order subtotal must be positive.");
        if (discount < 0 || discount >= subtotal)
            throw new InvalidOperationException("Order discount must be non-negative and less than the subtotal.");

        Id = id;
        OrderNumber = $"O{createdOnUtc:yyyyMMdd}{id:N}";
        UserId = userId.Trim();
        IdempotencyKey = idempotencyKey.Trim();
        RequestFingerprint = requestFingerprint;
        PaymentMethod = paymentMethod.Trim();
        Currency = currency.Trim().ToUpperInvariant();
        FlashSaleCampaignId = flashSaleCampaignId;
        PricingSnapshotId = pricingSnapshotId;
        PricingSnapshotHash = pricingSnapshotHash;
        PaymentDueOnUtc = paymentDueOnUtc;
        _lines.AddRange(orderLines);
        _discounts.AddRange(orderDiscounts);
        SubtotalAmount = subtotal;
        DiscountAmount = discount;
        Amount = Money.Round(subtotal - discount);
        Status = OrderStatus.Submitted;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        _statusHistory.Add(OrderStatusHistory.Initial(id, createdOnUtc));
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public Guid? FlashSaleCampaignId { get; private set; }
    public decimal SubtotalAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public Guid PricingSnapshotId { get; private set; }
    public string PricingSnapshotHash { get; private set; } = string.Empty;
    public DateTimeOffset PaymentDueOnUtc { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? ConfirmedOnUtc { get; private set; }
    public DateTimeOffset? CancelledOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public IReadOnlyCollection<OrderDiscount> Discounts => _discounts;
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory;

    public void EnsureSameRequest(string requestFingerprint)
    {
        if (!string.Equals(RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The idempotency key is already bound to a different order request.");
    }

    public void InventoryReserved(Guid reservationId, DateTimeOffset? changedOnUtc = null)
    {
        if (reservationId == Guid.Empty)
            throw new ArgumentException("Reservation id is required.", nameof(reservationId));
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Order is not awaiting inventory.");

        ReservationId = reservationId;
        TransitionTo(OrderStatus.AwaitingPayment, changedOnUtc ?? DateTimeOffset.UtcNow, null);
    }

    public void Confirm(Guid paymentId) =>
        Confirm(paymentId, Amount, Currency, PricingSnapshotHash, DateTimeOffset.UtcNow);

    public void Confirm(
        Guid paymentId,
        decimal capturedAmount,
        string currency,
        string? pricingSnapshotHash,
        DateTimeOffset changedOnUtc)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Order is not awaiting payment.");
        EnsurePaymentMatchesPriceSnapshot(capturedAmount, currency, pricingSnapshotHash);

        PaymentId = paymentId;
        ConfirmedOnUtc = changedOnUtc;
        TransitionTo(OrderStatus.Confirmed, changedOnUtc, null);
    }

    public void EnsurePaymentMatchesPriceSnapshot(
        decimal capturedAmount,
        string currency,
        string? pricingSnapshotHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (Money.Round(capturedAmount) != Amount
            || !string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase)
            || pricingSnapshotHash is not null
                && !string.Equals(PricingSnapshotHash, pricingSnapshotHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Captured payment does not match the immutable order price snapshot.");
        }
    }

    public void Cancel(string reason) => TryCancel(reason);

    public bool TryCancel(
        string reason,
        DateTimeOffset? changedOnUtc = null,
        string actor = "system")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
            return false;

        var now = changedOnUtc ?? DateTimeOffset.UtcNow;
        CancellationReason = reason.Trim();
        CancelledOnUtc = now;
        TransitionTo(OrderStatus.Cancelled, now, CancellationReason, actor);
        return true;
    }

    private void TransitionTo(
        OrderStatus next,
        DateTimeOffset changedOnUtc,
        string? reason,
        string actor = "system")
    {
        var previous = Status;
        Status = next;
        UpdatedOnUtc = changedOnUtc;
        Version++;
        _statusHistory.Add(new OrderStatusHistory(
            Guid.NewGuid(),
            Id,
            previous,
            next,
            reason,
            actor,
            changedOnUtc));
    }
}

public sealed class OrderLine
{
    private OrderLine()
    {
    }

    public OrderLine(string sku, string productName, int quantity, decimal unitPrice)
        : this(sku, productName, quantity, unitPrice, unitPrice)
    {
    }

    public OrderLine(
        string sku,
        string productName,
        int quantity,
        decimal listUnitPrice,
        decimal unitPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (listUnitPrice < 0 || unitPrice < 0 || unitPrice > listUnitPrice)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Sku = sku.Trim();
        ProductName = productName.Trim();
        Quantity = quantity;
        ListUnitPrice = Money.Round(listUnitPrice);
        UnitPrice = Money.Round(unitPrice);
        LineSubtotal = Money.Round(ListUnitPrice * quantity);
        DiscountAmount = Money.Round((ListUnitPrice - UnitPrice) * quantity);
        LineAmount = Money.Round(UnitPrice * quantity);
    }

    public long Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal ListUnitPrice { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineSubtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal LineAmount { get; private set; }
}

public sealed class OrderDiscount
{
    private OrderDiscount()
    {
    }

    public OrderDiscount(
        Guid id,
        Guid promotionId,
        long promotionRevision,
        Guid? couponId,
        string sourceType,
        string description,
        decimal amount,
        string snapshotJson)
    {
        if (id == Guid.Empty || promotionId == Guid.Empty)
            throw new ArgumentException("Discount and promotion ids are required.");
        if (promotionRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(promotionRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Id = id;
        PromotionId = promotionId;
        PromotionRevision = promotionRevision;
        CouponId = couponId;
        SourceType = sourceType;
        Description = description;
        Amount = Money.Round(amount);
        SnapshotJson = snapshotJson;
    }

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public long PromotionRevision { get; private set; }
    public Guid? CouponId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";
}

public sealed class OrderStatusHistory
{
    private OrderStatusHistory()
    {
    }

    public OrderStatusHistory(
        Guid id,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        Id = id;
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        Actor = actor;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }

    internal static OrderStatusHistory Initial(Guid orderId, DateTimeOffset now) =>
        new(Guid.NewGuid(), orderId, null, OrderStatus.Submitted, null, "system", now);
}

public enum OrderStatus
{
    Submitted = 0,
    AwaitingPayment = 1,
    Confirmed = 2,
    Cancelled = 3
}

internal static class Money
{
    public static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
