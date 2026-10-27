using MediatR;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Integration;

/// <summary>
/// 售后聚合产生的副作用统一在这里转成 Outbox 消息：人工操作、超时自动处理与回执处理器走同一条路径，
/// 退款、库存与通知不会因为入口不同而漏发。
/// </summary>
internal sealed class AfterSalesDomainEventHandlers(IOutboxStore outbox)
    : INotificationHandler<AfterSalesStatusChangedDomainEvent>,
      INotificationHandler<AfterSalesRefundRequestedDomainEvent>,
      INotificationHandler<AfterSalesStockRequestedDomainEvent>
{
    public Task Handle(AfterSalesStatusChangedDomainEvent notification, CancellationToken cancellationToken)
    {
        var request = notification.Request;
        var replacement = notification.Status == AfterSalesStatus.ReplacementShipped;
        outbox.AddIntegrationEvent(new AfterSalesStatusChangedIntegrationEvent
        {
            RequestId = request.Id,
            RequestNumber = request.RequestNumber,
            OrderId = request.OrderId,
            OrderNumber = request.OrderNumber,
            UserId = request.UserId,
            Type = request.Type.ToString(),
            PreviousStatus = notification.PreviousStatus?.ToString(),
            Status = notification.Status.ToString(),
            Note = notification.Note,
            ByCustomer = notification.Actor == request.UserId,
            RefundAmount = request.RefundAmount,
            Currency = request.Currency,
            ReturnAddress = notification.Status == AfterSalesStatus.AwaitingReturn ? request.ReturnAddress : null,
            CarrierName = replacement ? request.ReplacementCarrierName : null,
            TrackingNumber = replacement ? request.ReplacementTrackingNumber : null,
            OccurredOnUtc = notification.ChangedOnUtc
        });
        return Task.CompletedTask;
    }

    public Task Handle(AfterSalesRefundRequestedDomainEvent notification, CancellationToken cancellationToken)
    {
        outbox.AddIntegrationEvent(new RefundPaymentRequestedIntegrationEvent
        {
            RefundRequestId = notification.RefundRequestId,
            OrderId = notification.OrderId,
            PaymentId = notification.PaymentId,
            Amount = notification.Amount,
            Currency = notification.Currency,
            Reason = $"After-sales {notification.RequestNumber}",
            AfterSalesRequestNumber = notification.RequestNumber
        });
        return Task.CompletedTask;
    }

    public Task Handle(AfterSalesStockRequestedDomainEvent notification, CancellationToken cancellationToken)
    {
        outbox.AddIntegrationEvent(new AfterSalesStockRequestedIntegrationEvent
        {
            RequestId = notification.RequestId,
            RequestNumber = notification.RequestNumber,
            Operation = notification.Operation.ToString(),
            Items = notification.Items.Select(item => new AfterSalesStockItemContract(item.Sku, item.Quantity)).ToArray(),
            OperatorId = notification.Actor
        });
        return Task.CompletedTask;
    }
}

/// <summary>Inventory's answer to a replacement stock hold.</summary>
internal sealed class AfterSalesStockReservationResultHandler(
    Infrastructure.OrderDbContext db,
    IOutboxStore outbox,
    TimeProvider clock)
    : OrderEventHandler<AfterSalesStockReservationResultIntegrationEvent>(db, outbox, "ordering.after-sales-stock-result.v1")
{
    protected override async Task HandleCoreAsync(
        AfterSalesStockReservationResultIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var request = await Application.AfterSales.AfterSalesLocks.LockRequestAsync(Db, integrationEvent.RequestId, cancellationToken)
            ?? throw new InvalidOperationException($"After-sales request {integrationEvent.RequestId} was not found.");
        await Db.Entry(request).Collection(value => value.Lines).LoadAsync(cancellationToken);
        request.ApplyStockReservation(integrationEvent.Reserved, integrationEvent.Reason, clock.GetUtcNow());
    }
}
