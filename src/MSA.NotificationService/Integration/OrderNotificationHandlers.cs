using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.NotificationService.Domain;
using MSA.NotificationService.Infrastructure;

namespace MSA.NotificationService.Integration;

internal sealed class OrderConfirmedHandler(NotificationDbContext db) : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    public async Task HandleAsync(OrderConfirmedIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        if (!await db.TryBeginMessageAsync(message.Id, nameof(OrderConfirmedHandler), cancellationToken))
            return;

        db.Notifications.Add(new Notification(
            Guid.NewGuid(), message.UserId, "order-confirmed", "订单已确认",
            $"订单 {message.OrderId} 已确认，金额 {message.Amount} {message.Currency}。",
            $"order-confirmed:{message.OrderId}"));
        await db.SaveEntitiesAsync(cancellationToken);
    }
}

internal sealed class OrderCancelledHandler(NotificationDbContext db) : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    public async Task HandleAsync(OrderCancelledIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        if (!await db.TryBeginMessageAsync(message.Id, nameof(OrderCancelledHandler), cancellationToken))
            return;

        db.Notifications.Add(new Notification(
            Guid.NewGuid(), message.UserId, "order-cancelled", "订单已取消",
            $"订单 {message.OrderId} 已取消：{message.Reason}",
            $"order-cancelled:{message.OrderId}"));
        await db.SaveEntitiesAsync(cancellationToken);
    }
}
