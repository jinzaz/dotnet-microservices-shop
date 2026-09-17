using MediatR;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;

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
        outbox.AddIntegrationEvent(integrationEvent);
        return Task.CompletedTask;
    }
}
