using MediatR;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId, string UserId, string? Reason) : ICommand<CancelOrderResult>;

public sealed record CancelOrderResult(Guid OrderId, string Status, string FulfillmentStatus, bool Changed);

internal sealed class CancelOrderCommandHandler(
    IOrderRepository orders,
    ISagaStore sagas,
    OrderCancellationWorkflow cancellation,
    TimeProvider clock) : IRequestHandler<CancelOrderCommand, CancelOrderResult>
{
    internal const string DefaultReason = "Cancelled by customer.";

    public async Task<CancelOrderResult> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await orders.GetWithFulfillmentHistoryAsync(request.OrderId, cancellationToken);
        // Other users' orders are reported as missing so ids cannot be probed.
        if (order is null || !string.Equals(order.UserId, request.UserId, StringComparison.Ordinal))
            throw new KeyNotFoundException("Order was not found.");

        if (order.Status == OrderStatus.Cancelled)
            return Result(order, changed: false);
        // Paid orders leave the cancellable window; refunds belong to the after-sales flow.
        if (order.Status == OrderStatus.Confirmed)
            throw new InvalidOperationException("Paid orders cannot be cancelled; request after-sales service instead.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? DefaultReason : request.Reason.Trim();
        var saga = await sagas.FindAsync(order.Id, cancellationToken);
        var changed = await cancellation.CancelAsync(order, saga, reason, request.UserId, clock.GetUtcNow(), cancellationToken);
        return Result(order, changed);
    }

    private static CancelOrderResult Result(Order order, bool changed) =>
        new(order.Id, order.Status.ToString(), order.FulfillmentStatus.ToString(), changed);
}

/// <summary>
/// Cancels an unconfirmed order and starts compensation for whatever the saga already acquired
/// (inventory reservation, pending payment). Shared by customer cancellation and saga timeouts.
/// The caller must hold the order row lock inside the current transaction.
/// </summary>
internal sealed class OrderCancellationWorkflow(
    IOutboxStore outbox,
    ICouponRedemptionStore couponRedemptions)
{
    public async Task<bool> CancelAsync(
        Order order,
        SagaState? saga,
        string reason,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var data = saga is null ? null : OrderFulfillmentSagaData.Parse(saga.Data);
        var paymentMayExist = order.Status == OrderStatus.AwaitingPayment || data?.PaymentId is not null;
        if (!order.TryCancel(reason, now, actor))
            return false;
        await couponRedemptions.ReleaseAsync(order.Id, now, cancellationToken);

        var requiresInventory = order.ReservationId.HasValue;
        var requiresPayment = paymentMayExist;
        if (saga is not null && data is not null)
        {
            data = data with
            {
                ReservationId = order.ReservationId,
                RequiresInventoryCompensation = requiresInventory,
                InventoryCompensated = !requiresInventory,
                RequiresPaymentCompensation = requiresPayment,
                PaymentCompensated = !requiresPayment,
                FailureReason = reason
            };

            if (requiresInventory || requiresPayment)
            {
                saga.BeginCompensation(OrderFulfillmentSagaData.Compensating, JsonSerializer.Serialize(data), now);
                saga.ScheduleTimeout(now.AddMinutes(5), now);
                RepublishOutstandingCompensations(order, data, saga.State);
            }
            else
            {
                saga.Fail(reason, now);
            }
        }

        outbox.AddIntegrationEvent(new OrderCancelledIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Reason = reason
        });
        return true;
    }

    public void RepublishOutstandingCompensations(
        Order order,
        OrderFulfillmentSagaData data,
        string compensationState)
    {
        if (data.RequiresInventoryCompensation
            && !data.InventoryCompensated
            && order.ReservationId is Guid reservationId)
        {
            outbox.AddIntegrationEvent(new ReleaseInventoryIntegrationEvent
            {
                OrderId = order.Id,
                ReservationId = reservationId,
                Reason = data.FailureReason ?? "Order compensation."
            });
        }
        if (!data.RequiresPaymentCompensation || data.PaymentCompensated)
            return;

        if (compensationState == OrderFulfillmentSagaData.CompensatingRefund)
        {
            if (data.PaymentId is not Guid paymentId
                || data.RefundRequestId is not Guid refundRequestId)
            {
                throw new InvalidOperationException("Refund compensation is missing its stable payment identifiers.");
            }
            outbox.AddIntegrationEvent(new RefundPaymentRequestedIntegrationEvent
            {
                RefundRequestId = refundRequestId,
                OrderId = order.Id,
                PaymentId = paymentId,
                Amount = order.Amount,
                Currency = order.Currency,
                Reason = data.FailureReason ?? "Late payment compensation."
            });
        }
        else
        {
            outbox.AddIntegrationEvent(new CancelPaymentRequestedIntegrationEvent
            {
                OrderId = order.Id,
                Reason = data.FailureReason ?? "Order compensation."
            });
        }
    }
}
