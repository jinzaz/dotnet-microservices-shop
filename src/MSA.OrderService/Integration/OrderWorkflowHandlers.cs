using Microsoft.EntityFrameworkCore;using MSA.Contracts;using MSA.EventBus.Abstractions;using MSA.EventBusOutbox;using MSA.OrderService.Infrastructure;using MSA.Saga;using System.Text.Json;
namespace MSA.OrderService.Integration;
internal abstract class OrderEventHandler<T>(OrderDbContext db,IOutboxStore outbox):IIntegrationEventHandler<T> where T:IntegrationEvent
{public async Task HandleAsync(T e,CancellationToken ct=default){await using var tx=await db.BeginTransactionAsync(ct);if(!await db.TryBeginMessageAsync(e.Id,GetType().Name,ct)){await db.RollbackTransactionAsync(ct);return;}await HandleCore(e,ct);await db.CommitTransactionAsync(tx,ct);}protected abstract Task HandleCore(T e,CancellationToken ct);protected void Publish(IntegrationEvent e)=>outbox.Add(OutboxMessage.Create(e.GetType().AssemblyQualifiedName!,JsonSerializer.Serialize(e,e.GetType())));protected OrderDbContext Db=>db;}
internal sealed class ProductChangedHandler(OrderDbContext db,IOutboxStore o):OrderEventHandler<ProductChangedIntegrationEvent>(db,o){protected override async Task HandleCore(ProductChangedIntegrationEvent e,CancellationToken ct){var p=await Db.ProductProjections.SingleOrDefaultAsync(x=>x.ProductId==e.ProductId,ct);if(p is null)Db.Add(new Domain.OrderProductProjection(e.ProductId,e.Sku,e.Name,e.Price,e.Currency,e.IsActive));else p.Update(e.Name,e.Price,e.Currency,e.IsActive);}}
internal sealed class InventoryReservedHandler(OrderDbContext db,IOutboxStore o,ISagaStore sagas):OrderEventHandler<InventoryReservedIntegrationEvent>(db,o){protected override async Task HandleCore(InventoryReservedIntegrationEvent e,CancellationToken ct){var order=await Db.Orders.SingleAsync(x=>x.Id==e.OrderId,ct);if(order.Status==Domain.OrderStatus.Cancelled){Publish(new ReleaseInventoryIntegrationEvent{OrderId=order.Id,ReservationId=e.ReservationId,Reason="Order was cancelled before inventory reservation completed."});return;}if(order.Status!=Domain.OrderStatus.Submitted)return;order.InventoryReserved(e.ReservationId);var saga=await sagas.FindAsync(e.OrderId,ct)??throw new InvalidOperationException("Order saga missing.");var old=JsonDocument.Parse(saga.Data).RootElement;var method=old.TryGetProperty("PaymentMethod",out var m)?m.GetString()??"Alipay":"Alipay";saga.TransitionTo("AwaitingPayment",JsonSerializer.Serialize(new{e.ReservationId,PaymentMethod=method}),DateTimeOffset.UtcNow);Publish(new PaymentRequestedIntegrationEvent{OrderId=order.Id,UserId=order.UserId,Amount=order.Amount,Currency=order.Currency,PaymentMethod=method});}}
internal sealed class InventoryRejectedHandler(OrderDbContext db,IOutboxStore o,ISagaStore sagas):OrderEventHandler<InventoryRejectedIntegrationEvent>(db,o){protected override async Task HandleCore(InventoryRejectedIntegrationEvent e,CancellationToken ct){var order=await Db.Orders.SingleAsync(x=>x.Id==e.OrderId,ct);if(!order.TryCancel(e.Reason))return;var saga=await sagas.FindAsync(e.OrderId,ct);saga?.Fail(e.Reason,DateTimeOffset.UtcNow);Publish(new OrderCancelledIntegrationEvent{OrderId=order.Id,UserId=order.UserId,Reason=e.Reason});}}
internal sealed class PaymentSucceededHandler(OrderDbContext db,IOutboxStore o,ISagaStore sagas):OrderEventHandler<PaymentSucceededIntegrationEvent>(db,o){protected override async Task HandleCore(PaymentSucceededIntegrationEvent e,CancellationToken ct){var order=await Db.Orders.SingleAsync(x=>x.Id==e.OrderId,ct);if(order.Status is Domain.OrderStatus.Confirmed or Domain.OrderStatus.Cancelled)return;order.Confirm(e.PaymentId);var saga=await sagas.FindAsync(e.OrderId,ct);saga?.Complete(DateTimeOffset.UtcNow);if(order.ReservationId is Guid reservationId)Publish(new CommitInventoryIntegrationEvent{OrderId=order.Id,ReservationId=reservationId});Publish(new OrderConfirmedIntegrationEvent{OrderId=order.Id,UserId=order.UserId,Amount=order.Amount,Currency=order.Currency});}}
internal sealed class PaymentFailedHandler(OrderDbContext db,IOutboxStore o,ISagaStore sagas):OrderEventHandler<PaymentFailedIntegrationEvent>(db,o){protected override async Task HandleCore(PaymentFailedIntegrationEvent e,CancellationToken ct){var order=await Db.Orders.SingleAsync(x=>x.Id==e.OrderId,ct);if(!order.TryCancel(e.Reason))return;var saga=await sagas.FindAsync(e.OrderId,ct);saga?.Fail(e.Reason,DateTimeOffset.UtcNow);if(order.ReservationId is Guid reservation)Publish(new ReleaseInventoryIntegrationEvent{OrderId=order.Id,ReservationId=reservation,Reason=e.Reason});Publish(new OrderCancelledIntegrationEvent{OrderId=order.Id,UserId=order.UserId,Reason=e.Reason});}}
internal sealed class OrderSagaTimeoutHandler(OrderDbContext db,IOutboxStore outbox):ISagaTimeoutHandler{public string SagaType=>"OrderFulfillment";public async Task HandleTimeoutAsync(SagaState saga,CancellationToken ct){var order=await db.Orders.SingleAsync(x=>x.Id==saga.CorrelationId,ct);if(order.Status is Domain.OrderStatus.Confirmed or Domain.OrderStatus.Cancelled)return;const string reason="Order workflow timed out.";order.Cancel(reason);if(order.ReservationId is Guid id)Publish(new ReleaseInventoryIntegrationEvent{OrderId=order.Id,ReservationId=id,Reason=reason});Publish(new OrderCancelledIntegrationEvent{OrderId=order.Id,UserId=order.UserId,Reason=reason});saga.Fail(reason,DateTimeOffset.UtcNow);void Publish(IntegrationEvent e)=>outbox.Add(OutboxMessage.Create(e.GetType().AssemblyQualifiedName!,JsonSerializer.Serialize(e,e.GetType())));}}

internal sealed class FlashSaleCampaignOpenedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<FlashSaleCampaignOpenedIntegrationEvent>(db, outbox)
{
    protected override async Task HandleCore(FlashSaleCampaignOpenedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var projection = await Db.FlashSaleCampaignProjections
            .SingleOrDefaultAsync(value => value.CampaignId == integrationEvent.CampaignId, cancellationToken);
        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        if (projection is null)
        {
            Db.FlashSaleCampaignProjections.Add(new Domain.FlashSaleCampaignProjection(
                integrationEvent.CampaignId,
                integrationEvent.Sku,
                integrationEvent.StartsOnUtc,
                integrationEvent.EndsOnUtc,
                integrationEvent.PurchaseLimit,
                integrationEvent.Strategy,
                occurredOnUtc));
            return;
        }

        projection.ApplyOpen(
            integrationEvent.Sku,
            integrationEvent.StartsOnUtc,
            integrationEvent.EndsOnUtc,
            integrationEvent.PurchaseLimit,
            integrationEvent.Strategy,
            occurredOnUtc);
    }
}

internal sealed class FlashSaleCampaignClosedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<FlashSaleCampaignClosedIntegrationEvent>(db, outbox)
{
    protected override async Task HandleCore(FlashSaleCampaignClosedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var projection = await Db.FlashSaleCampaignProjections
            .SingleOrDefaultAsync(value => value.CampaignId == integrationEvent.CampaignId, cancellationToken);
        projection?.Close(integrationEvent.ClosedOnUtc);
    }
}
