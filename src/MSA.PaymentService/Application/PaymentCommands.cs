using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Mediator;
using MSA.PaymentCore;
using MSA.PaymentService.Domain;
using MSA.PaymentService.Infrastructure;
using System.Text.Json;

namespace MSA.PaymentService.Application;

public sealed record ConfirmPaymentCommand(
    string MerchantTradeNo,
    string ProviderTransactionId,
    decimal Amount,
    string Currency,
    PaymentChannel Channel,
    string? ProviderNotificationId = null,
    string? PayloadHash = null) : ICommand<Guid>;

internal sealed class ConfirmPaymentHandler(
    PaymentDbContext db,
    IOutboxStore outbox,
    TimeProvider clock,
    ILogger<ConfirmPaymentHandler> logger)
    : IRequestHandler<ConfirmPaymentCommand, Guid>
{
    public async Task<Guid> Handle(
        ConfirmPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var payment = await db.LockPaymentByMerchantTradeNoAsync(
                request.MerchantTradeNo.Trim(),
                cancellationToken)
            ?? throw new KeyNotFoundException("Payment not found.");
        var now = clock.GetUtcNow();
        var notification = PaymentNotificationRecord.Receive(
            payment.Id,
            request.Channel,
            request.MerchantTradeNo,
            request.ProviderTransactionId,
            request.Amount,
            request.Currency,
            request.ProviderNotificationId,
            request.PayloadHash,
            now);

        var existingNotification = await db.PaymentNotifications
            .SingleOrDefaultAsync(
                value => value.Channel == notification.Channel
                         && value.NotificationKey == notification.NotificationKey,
                cancellationToken);
        if (existingNotification is not null)
        {
            if (!existingNotification.HasSamePayload(notification))
            {
                const string reason = "A provider notification id was reused with a different payload.";
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    payment.Status,
                    payment.Status,
                    "NotificationDeduplicationConflict",
                    now,
                    reason));
                logger.LogCritical(
                    "Payment notification conflict for channel {Channel}, key {NotificationKey}, payment {PaymentId}.",
                    notification.Channel,
                    notification.NotificationKey,
                    payment.Id);
            }

            return payment.Id;
        }

        db.PaymentNotifications.Add(notification);
        var providerTransactionOwner = await db.Payments
            .AsNoTracking()
            .Where(value =>
                value.Id != payment.Id
                && value.Channel == request.Channel
                && value.ProviderTransactionId == request.ProviderTransactionId.Trim())
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var conflict = providerTransactionOwner.HasValue
            ? $"Provider transaction is already associated with payment {providerTransactionOwner.Value}."
            : payment.GetSuccessfulNotificationConflict(
                request.ProviderTransactionId,
                request.Amount,
                request.Currency,
                request.Channel);
        if (conflict is not null)
        {
            notification.MarkConflict(conflict, now);
            db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                payment.Id,
                payment.Status,
                payment.Status,
                "NotificationConflict",
                now,
                conflict));
            logger.LogError(
                "Verified payment notification conflicted with payment {PaymentId}: {Conflict}",
                payment.Id,
                conflict);
            return payment.Id;
        }

        var previousStatus = payment.Status;
        var changed = payment.MarkPaid(request.ProviderTransactionId, now);
        notification.MarkApplied(now);
        if (!changed)
            return payment.Id;

        db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
            payment.Id,
            previousStatus,
            payment.Status,
            "ProviderPaymentSucceeded",
            now));
        var integrationEvent = new PaymentSucceededIntegrationEvent
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            ProviderTransactionId = request.ProviderTransactionId.Trim(),
            CapturedAmount = payment.Amount,
            Currency = payment.Currency,
            PricingSnapshotId = payment.PricingSnapshotId,
            PricingSnapshotHash = payment.PricingSnapshotHash
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
        return payment.Id;
    }
}

public sealed record GetPaymentByOrderQuery(
    Guid OrderId,
    string UserId,
    bool CanReadAll) : IQuery<PaymentDto?>;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Channel,
    string Status,
    string? PaymentUrl,
    DateTimeOffset ExpiresOnUtc);

internal sealed class GetPaymentByOrderHandler(PaymentDbContext db)
    : IRequestHandler<GetPaymentByOrderQuery, PaymentDto?>
{
    public Task<PaymentDto?> Handle(
        GetPaymentByOrderQuery request,
        CancellationToken cancellationToken) =>
        db.Payments
            .AsNoTracking()
            .Where(value =>
                value.OrderId == request.OrderId
                && (request.CanReadAll || value.UserId == request.UserId))
            .Select(value => new PaymentDto(
                value.Id,
                value.OrderId,
                value.Amount,
                value.Currency,
                value.Channel.ToString(),
                value.Status.ToString(),
                value.PaymentUrl,
                value.ExpiresOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
