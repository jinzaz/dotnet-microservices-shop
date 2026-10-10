using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.NotificationService.Domain;
using MSA.NotificationService.Infrastructure;

namespace MSA.NotificationService.Integration;

/// <summary>
/// 售后进度通知：只通知买家没有亲自操作的变化（商家处理与系统超时处理），买家自己提交、撤销、填运单不再打扰。
/// </summary>
internal sealed class AfterSalesNotificationHandler(NotificationDbContext db)
    : IIntegrationEventHandler<AfterSalesStatusChangedIntegrationEvent>
{
    public async Task HandleAsync(AfterSalesStatusChangedIntegrationEvent message, CancellationToken cancellationToken = default)
    {
        if (!await db.TryBeginMessageAsync(message.Id, nameof(AfterSalesNotificationHandler), cancellationToken))
            return;
        var content = message.ByCustomer ? null : Compose(message);
        if (content is null)
            return;

        db.Notifications.Add(new Notification(
            Guid.NewGuid(),
            message.UserId,
            $"after-sales-{message.Status.ToLowerInvariant()}",
            content.Value.Subject,
            content.Value.Body,
            $"after-sales:{message.Id}"));
        await db.SaveEntitiesAsync(cancellationToken);
    }

    private static (string Subject, string Body)? Compose(AfterSalesStatusChangedIntegrationEvent message)
    {
        var number = message.RequestNumber;
        var note = string.IsNullOrWhiteSpace(message.Note) ? string.Empty : $"说明：{message.Note}";
        return message.Status switch
        {
            "AwaitingReturn" => ("售后申请已通过，请寄回商品", $"售后单 {number} 已通过审核，请按以下地址寄回商品并在售后详情中填写运单号：{message.ReturnAddress}"),
            "Approved" => ("补寄申请已通过", $"售后单 {number} 已通过审核，商家会尽快为你补寄商品。"),
            "Rejected" => ("售后申请未通过", $"售后单 {number} 未通过。商家回复：{message.Note}"),
            "ReturnReceived" => ("商家已收到退货", $"售后单 {number} 的退货已签收，正在为你处理。"),
            "Refunding" => ("退款已发起", $"售后单 {number} 的退款 {message.RefundAmount:0.00} {message.Currency} 已发起，将原路退回。{note}"),
            "Refunded" => ("退款成功", $"售后单 {number} 的退款 {message.RefundAmount:0.00} {message.Currency} 已原路退回，请留意到账。"),
            "ReplacementShipped" => ("新商品已发出", $"售后单 {number} 的商品已发出：{message.CarrierName} {message.TrackingNumber}。收到后请在售后详情中确认收货。"),
            "Cancelled" => ("售后已关闭", $"售后单 {number} 已关闭。{note}"),
            "Completed" => ("售后已完成", $"售后单 {number} 已完成。{note}"),
            _ => null
        };
    }
}
