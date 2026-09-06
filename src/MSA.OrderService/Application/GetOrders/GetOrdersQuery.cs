using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.GetOrders;

public sealed record OrderListItemDto(
    Guid Id,
    string OrderNumber,
    string UserId,
    decimal Amount,
    string Currency,
    string OrderStatus,
    string FulfillmentStatus,
    string RecipientName,
    string PhoneNumber,
    string? CarrierName,
    string? TrackingNumber,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc);

public sealed record GetOrdersQuery(
    string RequestingUserId,
    bool CanReadAll,
    string? UserIdFilter,
    OrderFulfillmentStatus? FulfillmentStatus,
    int Skip,
    int Take) : IQuery<IReadOnlyList<OrderListItemDto>>;
