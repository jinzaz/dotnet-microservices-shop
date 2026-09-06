using MSA.Infrastrucute.Entities;

namespace MSA.OrderService.Domain;

public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderDiscount> _discounts = [];
    private readonly List<OrderStatusHistory> _statusHistory = [];
    private readonly List<OrderFulfillmentStatusHistory> _fulfillmentStatusHistory = [];

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
            ShippingAddressSnapshot.Legacy(),
            lines,
            [],
            currency,
            flashSaleCampaignId,
            null,
            null,
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
        ShippingAddressSnapshot shippingAddress,
        IEnumerable<OrderLine> lines,
        IEnumerable<OrderDiscount> discounts,
        string currency,
        Guid? flashSaleCampaignId,
        Guid? sourceBasketId,
        long? sourceBasketVersion,
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
        ArgumentNullException.ThrowIfNull(shippingAddress);
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
        if (sourceBasketId.HasValue != sourceBasketVersion.HasValue)
            throw new ArgumentException("Basket id and basket version must be provided together.");
        if (sourceBasketId == Guid.Empty)
            throw new ArgumentException("Source basket id cannot be empty.", nameof(sourceBasketId));
        if (sourceBasketVersion is < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceBasketVersion));
        if (flashSaleCampaignId.HasValue && sourceBasketId.HasValue)
            throw new InvalidOperationException("Flash-sale orders cannot be created from a basket checkout.");

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
        ShippingAddress = shippingAddress;
        Currency = currency.Trim().ToUpperInvariant();
        FlashSaleCampaignId = flashSaleCampaignId;
        SourceBasketId = sourceBasketId;
        SourceBasketVersion = sourceBasketVersion;
        PricingSnapshotId = pricingSnapshotId;
        PricingSnapshotHash = pricingSnapshotHash;
        PaymentDueOnUtc = paymentDueOnUtc;
        _lines.AddRange(orderLines);
        _discounts.AddRange(orderDiscounts);
        SubtotalAmount = subtotal;
        DiscountAmount = discount;
        Amount = Money.Round(subtotal - discount);
        Status = OrderStatus.Submitted;
        FulfillmentStatus = OrderFulfillmentStatus.PendingPayment;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        _statusHistory.Add(OrderStatusHistory.Initial(id, createdOnUtc));
        _fulfillmentStatusHistory.Add(OrderFulfillmentStatusHistory.Initial(id, createdOnUtc));
        AddDomainEvent(new OrderFulfillmentStatusChangedDomainEvent(
            Id,
            UserId,
            null,
            FulfillmentStatus,
            null,
            null,
            null,
            createdOnUtc));
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string PaymentMethod { get; private set; } = string.Empty;
    public ShippingAddressSnapshot ShippingAddress { get; private set; } = null!;
    public Guid? FlashSaleCampaignId { get; private set; }
    public Guid? SourceBasketId { get; private set; }
    public long? SourceBasketVersion { get; private set; }
    public decimal SubtotalAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public Guid PricingSnapshotId { get; private set; }
    public string PricingSnapshotHash { get; private set; } = string.Empty;
    public DateTimeOffset PaymentDueOnUtc { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderFulfillmentStatus FulfillmentStatus { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? CarrierName { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? ConfirmedOnUtc { get; private set; }
    public DateTimeOffset? CancelledOnUtc { get; private set; }
    public DateTimeOffset? ShippedOnUtc { get; private set; }
    public DateTimeOffset? InTransitOnUtc { get; private set; }
    public DateTimeOffset? DeliveredOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;
    public IReadOnlyCollection<OrderDiscount> Discounts => _discounts;
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory;
    public IReadOnlyCollection<OrderFulfillmentStatusHistory> FulfillmentStatusHistory => _fulfillmentStatusHistory;

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
        TransitionFulfillmentTo(
            OrderFulfillmentStatus.AwaitingShipment,
            changedOnUtc,
            "Payment confirmed; order is ready for fulfillment.",
            "system",
            null,
            null);
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
        TransitionFulfillmentTo(
            OrderFulfillmentStatus.Cancelled,
            now,
            CancellationReason,
            actor,
            null,
            null);
        return true;
    }

    public bool Ship(
        string idempotencyKey,
        string requestFingerprint,
        string carrierCode,
        string carrierName,
        string trackingNumber,
        string? note,
        string actor,
        DateTimeOffset changedOnUtc)
    {
        if (IsFulfillmentOperationReplay(idempotencyKey, requestFingerprint))
            return false;
        if (Status != OrderStatus.Confirmed || FulfillmentStatus != OrderFulfillmentStatus.AwaitingShipment)
            throw new InvalidOperationException("Only a confirmed order awaiting shipment can be shipped.");

        CarrierCode = Required(carrierCode, 50, nameof(carrierCode)).ToUpperInvariant();
        CarrierName = Required(carrierName, 120, nameof(carrierName));
        TrackingNumber = Required(trackingNumber, 120, nameof(trackingNumber)).ToUpperInvariant();
        ShippedOnUtc = changedOnUtc;
        TransitionFulfillmentTo(
            OrderFulfillmentStatus.Shipped,
            changedOnUtc,
            Optional(note, 1000, nameof(note)),
            Required(actor, 200, nameof(actor)),
            Required(idempotencyKey, 100, nameof(idempotencyKey)),
            RequiredFingerprint(requestFingerprint));
        return true;
    }

    public bool MarkInTransit(
        string idempotencyKey,
        string requestFingerprint,
        string? note,
        string actor,
        DateTimeOffset changedOnUtc)
    {
        if (IsFulfillmentOperationReplay(idempotencyKey, requestFingerprint))
            return false;
        if (FulfillmentStatus != OrderFulfillmentStatus.Shipped)
            throw new InvalidOperationException("Only a shipped order can be marked in transit.");

        InTransitOnUtc = changedOnUtc;
        TransitionFulfillmentTo(
            OrderFulfillmentStatus.InTransit,
            changedOnUtc,
            Optional(note, 1000, nameof(note)),
            Required(actor, 200, nameof(actor)),
            Required(idempotencyKey, 100, nameof(idempotencyKey)),
            RequiredFingerprint(requestFingerprint));
        return true;
    }

    public bool MarkDelivered(
        string idempotencyKey,
        string requestFingerprint,
        string? note,
        string actor,
        DateTimeOffset changedOnUtc)
    {
        if (IsFulfillmentOperationReplay(idempotencyKey, requestFingerprint))
            return false;
        if (FulfillmentStatus != OrderFulfillmentStatus.InTransit)
            throw new InvalidOperationException("Only an in-transit order can be marked delivered.");

        DeliveredOnUtc = changedOnUtc;
        TransitionFulfillmentTo(
            OrderFulfillmentStatus.Delivered,
            changedOnUtc,
            Optional(note, 1000, nameof(note)),
            Required(actor, 200, nameof(actor)),
            Required(idempotencyKey, 100, nameof(idempotencyKey)),
            RequiredFingerprint(requestFingerprint));
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

    private bool IsFulfillmentOperationReplay(string idempotencyKey, string requestFingerprint)
    {
        var normalizedKey = Required(idempotencyKey, 100, nameof(idempotencyKey));
        var normalizedFingerprint = RequiredFingerprint(requestFingerprint);
        var existing = _fulfillmentStatusHistory.SingleOrDefault(history =>
            string.Equals(history.IdempotencyKey, normalizedKey, StringComparison.Ordinal));
        if (existing is null)
            return false;
        if (!string.Equals(existing.RequestFingerprint, normalizedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The fulfillment idempotency key is bound to a different request.");
        return true;
    }

    private void TransitionFulfillmentTo(
        OrderFulfillmentStatus next,
        DateTimeOffset changedOnUtc,
        string? note,
        string actor,
        string? idempotencyKey,
        string? requestFingerprint)
    {
        var previous = FulfillmentStatus;
        FulfillmentStatus = next;
        UpdatedOnUtc = changedOnUtc;
        Version++;
        _fulfillmentStatusHistory.Add(new OrderFulfillmentStatusHistory(
            Guid.NewGuid(),
            Id,
            previous,
            next,
            CarrierCode,
            CarrierName,
            TrackingNumber,
            note,
            actor,
            idempotencyKey,
            requestFingerprint,
            changedOnUtc));
        AddDomainEvent(new OrderFulfillmentStatusChangedDomainEvent(
            Id,
            UserId,
            previous,
            next,
            CarrierCode,
            CarrierName,
            TrackingNumber,
            changedOnUtc));
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string RequiredFingerprint(string value)
    {
        var fingerprint = Required(value, 64, nameof(value));
        if (fingerprint.Length != 64 || fingerprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Request fingerprint must be a SHA-256 hexadecimal value.", nameof(value));
        return fingerprint.ToUpperInvariant();
    }
}

public sealed class ShippingAddressSnapshot
{
    private ShippingAddressSnapshot()
    {
    }

    public ShippingAddressSnapshot(
        Guid? sourceAddressId,
        string recipientName,
        string phoneNumber,
        string countryCode,
        string province,
        string city,
        string district,
        string streetLine1,
        string? streetLine2,
        string? postalCode)
    {
        if (sourceAddressId == Guid.Empty)
            throw new ArgumentException("Source address id cannot be empty.", nameof(sourceAddressId));

        SourceAddressId = sourceAddressId;
        RecipientName = Required(recipientName, 100, nameof(recipientName));
        PhoneNumber = Required(phoneNumber, 32, nameof(phoneNumber));
        CountryCode = Required(countryCode, 3, nameof(countryCode)).ToUpperInvariant();
        if (CountryCode.Length is < 2 or > 3)
            throw new ArgumentException("Country code must contain two or three characters.", nameof(countryCode));
        Province = Required(province, 100, nameof(province));
        City = Required(city, 100, nameof(city));
        District = Required(district, 100, nameof(district));
        StreetLine1 = Required(streetLine1, 300, nameof(streetLine1));
        StreetLine2 = Optional(streetLine2, 300, nameof(streetLine2));
        PostalCode = Optional(postalCode, 20, nameof(postalCode));
    }

    public Guid? SourceAddressId { get; private set; }
    public string RecipientName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public string Province { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public string StreetLine1 { get; private set; } = string.Empty;
    public string? StreetLine2 { get; private set; }
    public string? PostalCode { get; private set; }

    internal static ShippingAddressSnapshot Legacy() =>
        new(null, "Legacy recipient", "UNKNOWN", "ZZ", "UNKNOWN", "UNKNOWN", "UNKNOWN", "UNKNOWN", null, null);

    private static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
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

public sealed class OrderFulfillmentStatusHistory
{
    private OrderFulfillmentStatusHistory()
    {
    }

    public OrderFulfillmentStatusHistory(
        Guid id,
        Guid orderId,
        OrderFulfillmentStatus? fromStatus,
        OrderFulfillmentStatus toStatus,
        string? carrierCode,
        string? carrierName,
        string? trackingNumber,
        string? note,
        string actor,
        string? idempotencyKey,
        string? requestFingerprint,
        DateTimeOffset occurredOnUtc)
    {
        Id = id;
        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        CarrierCode = carrierCode;
        CarrierName = carrierName;
        TrackingNumber = trackingNumber;
        Note = note;
        Actor = actor;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderFulfillmentStatus? FromStatus { get; private set; }
    public OrderFulfillmentStatus ToStatus { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? CarrierName { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? Note { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string? IdempotencyKey { get; private set; }
    public string? RequestFingerprint { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }

    internal static OrderFulfillmentStatusHistory Initial(Guid orderId, DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            orderId,
            null,
            OrderFulfillmentStatus.PendingPayment,
            null,
            null,
            null,
            "Order submitted.",
            "system",
            null,
            null,
            now);
}

public enum OrderStatus
{
    Submitted = 0,
    AwaitingPayment = 1,
    Confirmed = 2,
    Cancelled = 3
}

public enum OrderFulfillmentStatus
{
    PendingPayment = 0,
    AwaitingShipment = 1,
    Shipped = 2,
    InTransit = 3,
    Delivered = 4,
    Cancelled = 5
}

internal static class Money
{
    public static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
