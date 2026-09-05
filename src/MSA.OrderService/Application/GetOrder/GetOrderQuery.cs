using MSA.Infrastrucute.Mediator;

namespace MSA.OrderService.Application.GetOrder;

public sealed record OrderLineDto(
    string Sku,
    string ProductName,
    int Quantity,
    decimal ListUnitPrice,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal DiscountAmount,
    decimal LineAmount);

public sealed record OrderDiscountDto(
    Guid PromotionId,
    long PromotionRevision,
    Guid? CouponId,
    string SourceType,
    string Description,
    decimal Amount);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    string UserId,
    Guid? FlashSaleCampaignId,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string Status,
    Guid? ReservationId,
    Guid? PaymentId,
    string? CancellationReason,
    Guid PricingSnapshotId,
    DateTimeOffset PaymentDueOnUtc,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<OrderDiscountDto> Discounts,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc);

public sealed record GetOrderQuery(
    Guid OrderId,
    string UserId,
    bool CanReadAll) : IQuery<OrderDto?>;
