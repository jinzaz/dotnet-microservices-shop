using MSA.Infrastrucute.Events;

namespace MSA.OrderService.Domain;

public sealed record OrderFulfillmentStatusChangedDomainEvent(
    Guid OrderId,
    string UserId,
    OrderFulfillmentStatus? PreviousStatus,
    OrderFulfillmentStatus Status,
    string? CarrierCode,
    string? CarrierName,
    string? TrackingNumber,
    DateTimeOffset ChangedOnUtc) : DomainEvent;
