using MediatR;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;
using System.Text.Json;

namespace MSA.OrderService.Integration;

internal sealed class OrderFulfillmentDomainEventHandler(IOutboxStore outbox)
    : INotificationHandler<OrderFulfillmentStatusChangedDomainEvent>
{
    public Task Handle(
        OrderFulfillmentStatusChangedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        var integrationEvent = new OrderFulfillmentStatusChangedIntegrationEvent
        {
            OrderId = notification.OrderId,
            UserId = notification.UserId,
            PreviousStatus = notification.PreviousStatus?.ToString() ?? "None",
            Status = notification.Status.ToString(),
            CarrierCode = notification.CarrierCode,
            CarrierName = notification.CarrierName,
            TrackingNumber = notification.TrackingNumber,
            OccurredOnUtc = notification.ChangedOnUtc
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
        return Task.CompletedTask;
    }
}
