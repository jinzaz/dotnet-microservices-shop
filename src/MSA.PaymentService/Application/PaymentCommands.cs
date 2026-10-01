using MSA.Redis;
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

/// <summary>
/// 支付页等待期间每 3 秒轮询一次这里。按订单缓存（连同归属人，归属校验在内存里做），
/// 支付单任何一次落库（创建、回调成功、过期、取消）提交后失效，所以轮询到的状态不会比数据库慢。
/// 还没建支付单时的「没有」也缓存 —— 支付单一创建就被失效规则清掉。
/// </summary>
internal sealed class GetPaymentByOrderHandler(PaymentDbContext db, ICacheStore cache)
    : IRequestHandler<GetPaymentByOrderQuery, PaymentDto?>
{
    private static readonly CachePolicy Policy = new(TimeSpan.FromSeconds(30));

    public async Task<PaymentDto?> Handle(
        GetPaymentByOrderQuery request,
        CancellationToken cancellationToken)
    {
        var owned = await cache.GetOrLoadAsync(
            PaymentCacheKeys.ByOrder(request.OrderId),
            token => db.Payments
                .AsNoTracking()
                .Where(value => value.OrderId == request.OrderId)
                .Select(value => new OwnedPayment(
                    value.UserId,
                    new PaymentDto(
                        value.Id,
                        value.OrderId,
                        value.Amount,
                        value.Currency,
                        value.Channel.ToString(),
                        value.Status.ToString(),
                        value.PaymentUrl,
                        value.ExpiresOnUtc)))
                .SingleOrDefaultAsync(token),
            Policy,
            cancellationToken);
        return owned is not null && (request.CanReadAll || owned.UserId == request.UserId)
            ? owned.Payment
            : null;
    }

    internal sealed record OwnedPayment(string UserId, PaymentDto Payment);
}

/// <summary>支付服务的缓存键；服务前缀（<c>payment</c>）由 MSA.Redis 统一加。</summary>
public static class PaymentCacheKeys
{
    public static string ByOrder(Guid orderId) => $"payment-by-order:{orderId:N}";

    public static void Configure(CacheInvalidationRules rules) =>
        rules.On<Payment>((payment, change) => change.Key(ByOrder(payment.OrderId)));
}

public sealed record ListPaymentsQuery(
    PaymentStatus? Status,
    PaymentChannel? Channel,
    Guid? OrderId,
    string? UserId,
    int Skip,
    int Take) : IQuery<PaymentPage>;

public sealed record PaymentListItemDto(
    Guid Id,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Currency,
    string Channel,
    string Status,
    string MerchantTradeNo,
    string? ProviderTransactionId,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset ExpiresOnUtc,
    DateTimeOffset? PaidOnUtc,
    DateTimeOffset? ClosedOnUtc,
    decimal RefundedAmount,
    string? FailureReason);

public sealed record PaymentPage(
    IReadOnlyList<PaymentListItemDto> Items,
    int Skip,
    int Take,
    int Total);

internal sealed class ListPaymentsHandler(PaymentDbContext db)
    : IRequestHandler<ListPaymentsQuery, PaymentPage>
{
    public async Task<PaymentPage> Handle(
        ListPaymentsQuery request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 200);
        var query = db.Payments.AsNoTracking();
        if (request.Status is { } status)
            query = query.Where(payment => payment.Status == status);
        if (request.Channel is { } channel)
            query = query.Where(payment => payment.Channel == channel);
        if (request.OrderId is { } orderId)
            query = query.Where(payment => payment.OrderId == orderId);
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var userId = request.UserId.Trim();
            query = query.Where(payment => payment.UserId == userId);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(payment => payment.CreatedOnUtc)
            .Skip(skip)
            .Take(take)
            .Select(payment => new PaymentListItemSnapshot(
                payment.Id,
                payment.OrderId,
                payment.UserId,
                payment.Amount,
                payment.Currency,
                payment.Channel,
                payment.Status,
                payment.MerchantTradeNo,
                payment.ProviderTransactionId,
                payment.CreatedOnUtc,
                payment.ExpiresOnUtc,
                payment.PaidOnUtc,
                payment.ClosedOnUtc,
                payment.RefundedAmount,
                payment.FailureReason))
            .ToArrayAsync(cancellationToken);
        var items = rows.Select(payment => new PaymentListItemDto(
            payment.Id,
            payment.OrderId,
            payment.UserId,
            payment.Amount,
            payment.Currency,
            payment.Channel.ToString(),
            payment.Status.ToString(),
            payment.MerchantTradeNo,
            payment.ProviderTransactionId,
            payment.CreatedOnUtc,
            payment.ExpiresOnUtc,
            payment.PaidOnUtc,
            payment.ClosedOnUtc,
            payment.RefundedAmount,
            payment.FailureReason)).ToArray();
        return new PaymentPage(items, skip, take, total);
    }

    private sealed record PaymentListItemSnapshot(
        Guid Id,
        Guid OrderId,
        string UserId,
        decimal Amount,
        string Currency,
        PaymentChannel Channel,
        PaymentStatus Status,
        string MerchantTradeNo,
        string? ProviderTransactionId,
        DateTimeOffset CreatedOnUtc,
        DateTimeOffset ExpiresOnUtc,
        DateTimeOffset? PaidOnUtc,
        DateTimeOffset? ClosedOnUtc,
        decimal RefundedAmount,
        string? FailureReason);
}
