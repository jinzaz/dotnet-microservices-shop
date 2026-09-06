using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.ManageFulfillment;

public sealed record UpdateOrderFulfillmentCommand(
    Guid OrderId,
    string IdempotencyKey,
    OrderFulfillmentStatus TargetStatus,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingNumber,
    string? Note,
    string Actor) : ICommand<OrderFulfillmentCommandResult>;

public sealed record OrderFulfillmentCommandResult(
    Guid OrderId,
    OrderFulfillmentStatus Status,
    bool Changed,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingNumber,
    DateTimeOffset UpdatedOnUtc);
