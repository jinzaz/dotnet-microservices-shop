using MediatR;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Integration;

internal sealed class OrderFulfillmentDomainEventHandler(IOutboxStore outbox)
    : INotificationHandler<OrderFulfillmentStatusChangedDomainEvent>
{
    /// <summary>
    /// 只发布有订阅方关心的状态。目前唯一的订阅方是通知服务（OrderNotificationHandlers），只处理这三种；
    /// 建单、付款、取消每单都会触发，发出去没人收，只会挤占 Outbox。
    /// 将来有新订阅方需要其他状态时，必须同步更新这里。
    /// </summary>
    internal static readonly IReadOnlySet<OrderFulfillmentStatus> PublishedStatuses = new HashSet<OrderFulfillmentStatus>
    {
        OrderFulfillmentStatus.Shipped,
        OrderFulfillmentStatus.Delivered,
        OrderFulfillmentStatus.ReturnedToSender
    };

    public Task Handle(
        OrderFulfillmentStatusChangedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        if (!PublishedStatuses.Contains(notification.Status))
            return Task.CompletedTask;

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
