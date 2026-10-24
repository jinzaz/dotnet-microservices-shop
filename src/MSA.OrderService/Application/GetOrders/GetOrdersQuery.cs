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
    DateTimeOffset UpdatedOnUtc,
    /// <summary>Total refunded through after-sales; equal to <see cref="Amount"/> once fully refunded.</summary>
    decimal RefundedAmount);

/// <param name="Search">Admin-only: order id, order number fragment, recipient phone or tracking number.</param>
public sealed record GetOrdersQuery(
    string RequestingUserId,
    bool CanReadAll,
    string? UserIdFilter,
    OrderFulfillmentStatus? FulfillmentStatus,
    int Skip,
    int Take,
    string? Search = null) : IQuery<IReadOnlyList<OrderListItemDto>>;
