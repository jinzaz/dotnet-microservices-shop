using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;
using System.Text.Json;

namespace MSA.PaymentService.Integration;

internal sealed class CancelPaymentRequestedHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    TimeProvider clock,
    ILogger<CancelPaymentRequestedHandler> logger)
    : IIntegrationEventHandler<CancelPaymentRequestedIntegrationEvent>
{
    private const string ConsumerId = "payment.cancel-payment-requested.v1";

    public async Task HandleAsync(
        CancelPaymentRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await db.AcquireOrderTransactionLockAsync(
                integrationEvent.OrderId,
                cancellationToken);
            var payment = await db.LockPaymentByOrderIdAsync(
                integrationEvent.OrderId,
                cancellationToken);
            if (payment is null)
            {
                var intentExists = await db.PaymentCancellationIntents
                    .AnyAsync(
                        intent => intent.OrderId == integrationEvent.OrderId,
                        cancellationToken);
                if (!intentExists)
                {
                    db.PaymentCancellationIntents.Add(PaymentCancellationIntent.Create(
                        integrationEvent.OrderId,
                        integrationEvent.Id,
                        integrationEvent.Reason,
                        now));
                }
                PublishCancelled(integrationEvent.OrderId, Guid.Empty, now);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }
            if (payment.Status == PaymentStatus.Closed)
            {
                PublishCancelled(payment, now);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }
            if (payment.Status is PaymentStatus.Succeeded
                or PaymentStatus.Refunding
                or PaymentStatus.Refunded)
            {
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    payment.Status,
                    payment.Status,
                    "CancellationRejectedAfterCapture",
                    now,
                    "Captured payments must be compensated through a refund."));
                logger.LogWarning(
                    "Cancellation for captured payment {PaymentId} requires a refund.",
                    payment.Id);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            var previousStatus = payment.Status;
            if (payment.Channel == PaymentChannel.Balance)
            {
                // 余额支付没有外部交易要关闭，未扣款的支付单直接关单。
                payment.RequestCancellation(integrationEvent.Reason);
                payment.MarkCancelled(integrationEvent.Reason, now);
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    previousStatus,
                    payment.Status,
                    "BalancePaymentCancelled",
                    now,
                    integrationEvent.Reason));
                PublishCancelled(payment, now);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            if (payment.RequestCancellation(integrationEvent.Reason))
            {
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    previousStatus,
                    payment.Status,
                    "PaymentCancellationRequested",
                    now,
                    integrationEvent.Reason));
            }

            var operation = await db.ProviderOperations.SingleOrDefaultAsync(
                operation =>
                    operation.PaymentId == payment.Id
                    && operation.Type == PaymentProviderOperationType.CancelPayment,
                cancellationToken);
            if (operation is null)
            {
                db.ProviderOperations.Add(PaymentProviderOperation.CancelPayment(
                    payment.Id,
                    payment.MerchantTradeNo,
                    integrationEvent.Reason,
                    now));
            }
            else if ((operation.Status is PaymentProviderOperationStatus.Failed
                      or PaymentProviderOperationStatus.Cancelled)
                     && operation.Requeue(integrationEvent.Reason, now))
            {
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    payment.Status,
                    payment.Status,
                    "PaymentCancellationRequeued",
                    now,
                    integrationEvent.Reason));
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private void PublishCancelled(Payment payment, DateTimeOffset now)
        => PublishCancelled(payment.OrderId, payment.Id, now);

    private void PublishCancelled(Guid orderId, Guid paymentId, DateTimeOffset now)
    {
        var integrationEvent = new PaymentCancelledIntegrationEvent
        {
            OrderId = orderId,
            PaymentId = paymentId
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
    }
}

internal sealed class RefundPaymentRequestedHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    BalanceLedger ledger,
    TimeProvider clock)
    : IIntegrationEventHandler<RefundPaymentRequestedIntegrationEvent>
{
    private const string ConsumerId = "payment.refund-payment-requested.v1";

    public async Task HandleAsync(
        RefundPaymentRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(integrationEvent.Id, ConsumerId, cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            var payment = await db.LockPaymentAsync(
                integrationEvent.PaymentId,
                cancellationToken);
            if (payment.OrderId != integrationEvent.OrderId)
                throw new InvalidOperationException("Refund order does not match the payment order.");
            if (!string.Equals(
                    payment.Currency,
                    Payment.NormalizeCurrency(integrationEvent.Currency),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refund currency does not match the payment currency.");
            }

            var existingRefund = await db.LockRefundByRequestIdAsync(
                integrationEvent.RefundRequestId,
                cancellationToken);
            if (existingRefund is not null)
            {
                existingRefund.EnsureMatchesRequest(
                    payment.Id,
                    payment.OrderId,
                    integrationEvent.Amount,
                    integrationEvent.Currency,
                    integrationEvent.Reason);
                await RestoreOrReplayAsync(
                    existingRefund,
                    payment,
                    now,
                    cancellationToken);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return;
            }

            var refund = PaymentRefund.Request(
                payment.Id,
                payment.OrderId,
                integrationEvent.RefundRequestId,
                integrationEvent.Id,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.Reason,
                now);
            var previousStatus = payment.Status;
            if (!payment.RequestRefund(refund.Amount))
                throw new InvalidOperationException("Another refund is already in progress.");

            db.PaymentRefunds.Add(refund);
            db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                payment.Id,
                previousStatus,
                payment.Status,
                "PaymentRefundRequested",
                now,
                integrationEvent.Reason));
            if (payment.Channel == PaymentChannel.Balance)
            {
                // 余额支付原路退回余额：入账、退款单成功、PaymentRefunded 同一事务提交，不经过渠道调度器。
                var entry = await ledger.CreditRefundAsync(
                    payment,
                    refund,
                    integrationEvent.AfterSalesRequestNumber,
                    now,
                    cancellationToken);
                refund.MarkSucceeded(entry.Id.ToString("N"), now);
                payment.CompleteRefund(refund.Amount);
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    PaymentStatus.Refunding,
                    payment.Status,
                    "BalanceRefunded",
                    now,
                    $"Refund {refund.Id} credited to balance."));
                Publish(new PaymentRefundedIntegrationEvent
                {
                    OrderId = payment.OrderId,
                    PaymentId = payment.Id,
                    RefundId = refund.Id,
                    RefundRequestId = refund.RefundRequestId
                }, now);
            }
            else
            {
                db.ProviderOperations.Add(PaymentProviderOperation.RefundPayment(
                    payment.Id,
                    refund.Id,
                    refund.MerchantRefundNo,
                    now));
            }
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task RestoreOrReplayAsync(
        PaymentRefund refund,
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (refund.Status)
        {
            case PaymentRefundStatus.Requested:
                var operationExists = await db.ProviderOperations.AnyAsync(
                    operation =>
                        operation.Type == PaymentProviderOperationType.RefundPayment
                        && operation.BusinessReferenceId == refund.Id,
                    cancellationToken);
                if (!operationExists)
                {
                    db.ProviderOperations.Add(PaymentProviderOperation.RefundPayment(
                        payment.Id,
                        refund.Id,
                        refund.MerchantRefundNo,
                        now));
                }
                break;
            case PaymentRefundStatus.Succeeded:
                Publish(new PaymentRefundedIntegrationEvent
                {
                    OrderId = payment.OrderId,
                    PaymentId = payment.Id,
                    RefundId = refund.Id,
                    RefundRequestId = refund.RefundRequestId
                }, now);
                break;
            case PaymentRefundStatus.Failed:
                Publish(new PaymentRefundFailedIntegrationEvent
                {
                    OrderId = payment.OrderId,
                    PaymentId = payment.Id,
                    RefundId = refund.Id,
                    RefundRequestId = refund.RefundRequestId,
                    Reason = refund.FailureReason ?? "Payment refund failed."
                }, now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(refund.Status));
        }
    }

    private void Publish(IntegrationEvent integrationEvent, DateTimeOffset now) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
}
