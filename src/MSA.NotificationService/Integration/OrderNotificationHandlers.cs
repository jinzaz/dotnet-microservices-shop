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

/// <summary>
/// 履约进度通知：只通知发货、签收、退回这类买家需要知道的变化。建单（待付款）、支付成功（待发货）和取消
/// 已有确认 / 取消通知，运输中不再打扰；这些直接确认消费，不写 Inbox——每单都会发，不能白占一次写库。
/// </summary>
internal sealed class OrderFulfillmentStatusChangedHandler(NotificationDbContext db)
    : IIntegrationEventHandler<OrderFulfillmentStatusChangedIntegrationEvent>
{
    public async Task HandleAsync(OrderFulfillmentStatusChangedIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        var content = Compose(message);
        if (content is null)
            return;
        if (!await db.TryBeginMessageAsync(message.Id, nameof(OrderFulfillmentStatusChangedHandler), cancellationToken))
            return;

        db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            message.UserId,
            $"order-{message.Status.ToLowerInvariant()}",
            content.Value.Subject,
            content.Value.Body,
            $"order-fulfillment:{message.Id}"));
        await db.SaveEntitiesAsync(cancellationToken);
    }

    private static (string Subject, string Body)? Compose(OrderFulfillmentStatusChangedIntegrationEvent message) => message.Status switch
    {
        "Shipped" => ("订单已发货", $"订单 {message.OrderId} 已发货：{message.CarrierName} {message.TrackingNumber}。"),
        "Delivered" => ("订单已签收", $"订单 {message.OrderId} 已签收，如有问题可在订单详情中申请售后。"),
        "ReturnedToSender" => ("包裹已退回商家", $"订单 {message.OrderId} 已全额退款，包裹正在退回商家。"),
        _ => null
    };
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
