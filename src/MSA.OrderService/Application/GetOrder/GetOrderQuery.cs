using MSA.Infrastrucute.Mediator;

namespace MSA.OrderService.Application.GetOrder;

/// <summary>One immutable line in an order response.</summary>
public sealed record OrderLineDto(string Sku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>Current state of an order and its asynchronous fulfillment workflow.</summary>
public sealed record OrderDto(
    Guid Id,
    string UserId,
    Guid? FlashSaleCampaignId,
    decimal Amount,
    string Currency,
    string Status,
    Guid? ReservationId,
    Guid? PaymentId,
    string? CancellationReason,
    IReadOnlyList<OrderLineDto> Lines,
    DateTimeOffset CreatedOnUtc);

public sealed record GetOrderQuery(Guid OrderId, string UserId, bool CanReadAll) : IQuery<OrderDto?>;
