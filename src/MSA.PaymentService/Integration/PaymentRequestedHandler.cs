using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.PaymentCore;
using MSA.PaymentService.Application.Realtime;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;
using System.Text.Json;

namespace MSA.PaymentService.Integration;

internal sealed class PaymentRequestedHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    TimeProvider clock,
    OrderPaymentPrecheck precheck,
    IOptions<PaymentProcessingOptions> configured)
    : IIntegrationEventHandler<PaymentRequestedIntegrationEvent>
{
    private const string ConsumerId = "payment.payment-requested.v1";

    public async Task HandleAsync(
        PaymentRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var channel = ParseChannel(integrationEvent.PaymentMethod);
        var now = clock.GetUtcNow();
        // 先核对再开事务：跨服务调用不该占着数据库事务和订单锁。
        var rejection = await precheck.RejectAsync(
            integrationEvent.OrderId,
            integrationEvent.Amount,
            integrationEvent.Currency,
            integrationEvent.PricingSnapshotHash,
            cancellationToken);
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

            var payment = await db.Payments
                .SingleOrDefaultAsync(
                    value => value.OrderId == integrationEvent.OrderId,
                    cancellationToken);
            if (payment is null)
            {
                var expiresOnUtc = integrationEvent.PaymentDueOnUtc
                    ?? now.Add(configured.Value.PaymentLifetime);
                payment = new Payment(
                    Guid.NewGuid(),
                    integrationEvent.OrderId,
                    integrationEvent.UserId,
                    integrationEvent.Amount,
                    integrationEvent.Currency,
                    channel,
                    now,
                    expiresOnUtc,
                    integrationEvent.PricingSnapshotId,
                    integrationEvent.PricingSnapshotHash);
                db.Payments.Add(payment);
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    null,
                    payment.Status,
                    "PaymentRequested",
                    now));

                var cancellationIntent = await db.PaymentCancellationIntents
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        intent => intent.OrderId == integrationEvent.OrderId,
                        cancellationToken);
                if (rejection is not null)
                {
                    // 订单此刻的事实与事件不一致：不要把这笔钱收进来，直接判失败让 Saga 去补偿。
                    var previousStatus = payment.Status;
                    payment.Fail(rejection);
                    db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                        payment.Id,
                        previousStatus,
                        payment.Status,
                        "OrderConfirmationRejected",
                        now,
                        rejection));
                    PublishFailed(payment, rejection, now);
                }
                else if (cancellationIntent is not null)
                {
                    var previousStatus = payment.Status;
                    payment.RequestCancellation(cancellationIntent.Reason);
                    payment.MarkCancelled(cancellationIntent.Reason, now);
                    db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                        payment.Id,
                        previousStatus,
                        payment.Status,
                        "CancellationPrecededPaymentRequest",
                        now,
                        cancellationIntent.Reason));
                    PublishCancelled(payment, now);
                }
                else if (expiresOnUtc <= now)
                {
                    const string reason = "Payment request expired before it was processed.";
                    var previousStatus = payment.Status;
                    payment.Expire(now, reason);
                    db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                        payment.Id,
                        previousStatus,
                        payment.Status,
                        "PaymentRequestExpired",
                        now,
                        reason));
                    PublishFailed(payment, reason, now);
                }
                else
                {
                    db.ProviderOperations.Add(PaymentProviderOperation.CreatePayment(
                        payment.Id,
                        payment.MerchantTradeNo,
                        now));
                }
            }
            else
            {
                payment.EnsureMatchesRequest(
                    integrationEvent.UserId,
                    integrationEvent.Amount,
                    integrationEvent.Currency,
                    channel,
                    integrationEvent.PricingSnapshotId,
                    integrationEvent.PricingSnapshotHash,
                    integrationEvent.PaymentDueOnUtc);

                var operationExists = await db.ProviderOperations.AnyAsync(
                    operation =>
                        operation.PaymentId == payment.Id
                        && operation.Type == PaymentProviderOperationType.CreateNativePayment,
                    cancellationToken);
                if (!operationExists
                    && payment.Status == PaymentStatus.Pending
                    && payment.PaymentUrl is null)
                {
                    db.ProviderOperations.Add(PaymentProviderOperation.CreatePayment(
                        payment.Id,
                        payment.MerchantTradeNo,
                        now));
                }
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private void PublishFailed(Payment payment, string reason, DateTimeOffset now)
    {
        var failed = new PaymentFailedIntegrationEvent
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Reason = reason
        };
        outbox.Add(OutboxMessage.Create(
            failed.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(failed, failed.GetType()),
            now));
    }

    private static PaymentChannel ParseChannel(string value)
    {
        if (!Enum.TryParse<PaymentChannel>(value, ignoreCase: true, out var channel)
            || !Enum.IsDefined(channel))
        {
            throw new InvalidOperationException($"Unsupported payment method '{value}'.");
        }

        return channel;
    }

    private void PublishCancelled(Payment payment, DateTimeOffset now)
    {
        var integrationEvent = new PaymentCancelledIntegrationEvent
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
    }
}

public sealed class PaymentEndpointOptions
{
    public string NotifyBaseUrl { get; set; } =
        "https://localhost:10005/payment/callbacks";
}
