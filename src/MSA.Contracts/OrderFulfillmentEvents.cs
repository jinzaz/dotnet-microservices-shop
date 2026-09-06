using MSA.EventBus.Abstractions;

namespace MSA.Contracts;

public sealed class OrderFulfillmentStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required string PreviousStatus { get; init; }
    public required string Status { get; init; }
    public string? CarrierCode { get; init; }
    public string? CarrierName { get; init; }
    public string? TrackingNumber { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }
}
