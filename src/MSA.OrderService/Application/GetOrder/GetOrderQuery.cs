using MSA.Infrastrucute.Mediator;

namespace MSA.OrderService.Application.GetOrder;

public sealed record OrderLineDto(
    Guid? ProductId,
    Guid? VariantId,
    Guid? OfferId,
    string Sku,
    string ProductName,
    string VariantName,
    string VariantAttributesJson,
    int Quantity,
    decimal ListUnitPrice,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal DiscountAmount,
    decimal LineAmount,
    /// <summary>Units refunded through after-sales; unshipped orders ship only the rest.</summary>
    int RefundedQuantity = 0);

public sealed record OrderDiscountDto(
    Guid PromotionId,
    long PromotionRevision,
    Guid? CouponId,
    string SourceType,
    string Description,
    decimal Amount);

public sealed record ShippingAddressDto(
    Guid? AddressId,
    string RecipientName,
    string PhoneNumber,
    string CountryCode,
    string Province,
    string City,
    string District,
    string StreetLine1,
    string? StreetLine2,
    string? PostalCode);

public sealed record OrderFulfillmentStatusHistoryDto(
    string? FromStatus,
    string ToStatus,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingNumber,
    string? Note,
    string Actor,
    DateTimeOffset OccurredOnUtc);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    string UserId,
    Guid? FlashSaleCampaignId,
    Guid? SourceBasketId,
    long? SourceBasketVersion,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status,
    string FulfillmentStatus,
    Guid? ReservationId,
    Guid? PaymentId,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingNumber,
    string? CancellationReason,
    Guid PricingSnapshotId,
    DateTimeOffset PaymentDueOnUtc,
    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<OrderDiscountDto> Discounts,
    IReadOnlyList<OrderFulfillmentStatusHistoryDto> FulfillmentHistory,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    DateTimeOffset? ShippedOnUtc,
    DateTimeOffset? InTransitOnUtc,
    DateTimeOffset? DeliveredOnUtc,
    /// <summary>
    /// 支付服务此刻的状态。订单自身的状态要等支付成功事件送达才会变，
    /// 这一项让「刚付完、事件还在路上」的订单也能显示正确结果；问不到时为 null。
    /// </summary>
    string? LivePaymentStatus = null,
    /// <summary>Total refunded through after-sales; equal to <see cref="Amount"/> once fully refunded.</summary>
    decimal RefundedAmount = 0);

public sealed record GetOrderQuery(
    Guid OrderId,
    string UserId,
    bool CanReadAll) : IQuery<OrderDto?>;
