using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.PaymentCore;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;
using System.Text.Json;

namespace MSA.PaymentService.Integration;

internal sealed class PaymentRequestedHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    TimeProvider clock,
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
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var inboxRows = await db.TryInsertInboxMessageAsync(
                integrationEvent.Id,
                ConsumerId,
                now,
                cancellationToken);
            if (inboxRows == 0)
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
                if (cancellationIntent is not null)
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
