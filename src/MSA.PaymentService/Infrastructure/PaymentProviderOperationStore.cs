using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.PaymentCore;
using MSA.PaymentService.Domain;
using System.Text.Json;

namespace MSA.PaymentService.Infrastructure;

internal sealed record PaymentProviderOperationLease(Guid OperationId, Guid LockId);

internal sealed record PaymentProviderOperationSnapshot(
    Guid OperationId,
    Guid LockId,
    PaymentProviderOperationType OperationType,
    string IdempotencyKey,
    Guid? BusinessReferenceId,
    string? OperationReason,
    Guid PaymentId,
    int AttemptCount,
    Guid OrderId,
    string MerchantTradeNo,
    decimal Amount,
    string Currency,
    PaymentChannel Channel,
    PaymentStatus PaymentStatus,
    string? ProviderTransactionId,
    string? PaymentUrl,
    DateTimeOffset ExpiresOnUtc,
    Guid? RefundId = null,
    string? MerchantRefundNo = null,
    decimal? RefundAmount = null,
    string? RefundCurrency = null,
    string? RefundReason = null);

internal sealed class PaymentProviderOperationStore(
    PaymentDbContext db,
    IOutboxStore outbox,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<PaymentProviderOperationLease>> ClaimDueAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lockId = Guid.NewGuid();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operations = await db.ProviderOperations
                .FromSqlInterpolated(
                    $"""
                    SELECT operation.*
                    FROM payment_provider_operations AS operation
                    WHERE
                        (operation."Status" = {(int)PaymentProviderOperationStatus.Pending}
                         AND operation."NextAttemptOnUtc" <= {now})
                        OR
                        (operation."Status" = {(int)PaymentProviderOperationStatus.Processing}
                         AND operation."LockedUntilUtc" < {now})
                    ORDER BY operation."NextAttemptOnUtc", operation."CreatedOnUtc", operation."Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .AsTracking()
                .ToListAsync(cancellationToken);

            foreach (var operation in operations)
                operation.Claim(lockId, now.Add(lockDuration), now);

            await db.CommitTransactionAsync(transaction, cancellationToken);
            return operations
                .Select(operation => new PaymentProviderOperationLease(operation.Id, lockId))
                .ToArray();
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<PaymentProviderOperationSnapshot?> GetSnapshotAsync(
        PaymentProviderOperationLease lease,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.ProviderOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Id == lease.OperationId
                && operation.Status == PaymentProviderOperationStatus.Processing
                && operation.LockId == lease.LockId)
            .Join(
                db.Payments.AsNoTracking(),
                operation => operation.PaymentId,
                payment => payment.Id,
                (operation, payment) => new PaymentProviderOperationSnapshot(
                    operation.Id,
                    lease.LockId,
                    operation.Type,
                    operation.IdempotencyKey,
                    operation.BusinessReferenceId,
                    operation.Reason,
                    payment.Id,
                    operation.AttemptCount,
                    payment.OrderId,
                    payment.MerchantTradeNo,
                    payment.Amount,
                    payment.Currency,
                    payment.Channel,
                    payment.Status,
                    payment.ProviderTransactionId,
                    payment.PaymentUrl,
                    payment.ExpiresOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot?.OperationType != PaymentProviderOperationType.RefundPayment)
            return snapshot;
        if (!snapshot.BusinessReferenceId.HasValue)
            throw new InvalidOperationException(
                $"Refund provider operation {snapshot.OperationId} has no refund reference.");

        var refund = await db.PaymentRefunds
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == snapshot.BusinessReferenceId.Value,
                cancellationToken);
        return snapshot with
        {
            RefundId = refund.Id,
            MerchantRefundNo = refund.MerchantRefundNo,
            RefundAmount = refund.Amount,
            RefundCurrency = refund.Currency,
            RefundReason = refund.Reason
        };
    }

    public async Task<bool> CompleteCreateAsync(
        PaymentProviderOperationLease lease,
        CreatePaymentResult result,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operation = await db.LockProviderOperationAsync(lease.OperationId, cancellationToken);
            if (operation is null || !operation.IsOwnedBy(lease.LockId))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }
            if (operation.Type != PaymentProviderOperationType.CreateNativePayment)
                throw new InvalidOperationException($"Provider operation {operation.Id} is not a create operation.");

            var payment = await db.LockPaymentAsync(operation.PaymentId, cancellationToken);
            if (payment.Status == PaymentStatus.Pending && payment.SetPaymentUrl(result.PaymentUrl))
            {
                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    payment.Status,
                    payment.Status,
                    "ProviderPaymentCreated",
                    now));
            }

            operation.Complete(lease.LockId, now);
            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> CompleteCancellationAsync(
        PaymentProviderOperationLease lease,
        CancelPaymentResult result,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operation = await db.LockProviderOperationAsync(lease.OperationId, cancellationToken);
            if (operation is null || !operation.IsOwnedBy(lease.LockId))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }
            if (operation.Type != PaymentProviderOperationType.CancelPayment)
                throw new InvalidOperationException($"Provider operation {operation.Id} is not a cancellation operation.");

            var payment = await db.LockPaymentAsync(operation.PaymentId, cancellationToken);
            if (payment.Status != PaymentStatus.Cancelling)
            {
                operation.Cancel(
                    lease.LockId,
                    "Payment state changed while provider cancellation was in progress.",
                    now);
                await db.CommitTransactionAsync(transaction, cancellationToken);
                return true;
            }

            var previousStatus = payment.Status;
            var reason = operation.Reason ?? "Payment cancellation requested.";
            payment.MarkCancelled(reason, now);
            operation.Complete(lease.LockId, now);
            db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                payment.Id,
                previousStatus,
                payment.Status,
                "ProviderPaymentCancelled",
                now,
                reason));
            var integrationEvent = new PaymentCancelledIntegrationEvent
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id
            };
            outbox.Add(OutboxMessage.Create(
                integrationEvent.GetType().AssemblyQualifiedName!,
                JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
                now));
            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> CompleteRefundAsync(
        PaymentProviderOperationLease lease,
        RefundPaymentResult result,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operation = await db.LockProviderOperationAsync(lease.OperationId, cancellationToken);
            if (operation is null || !operation.IsOwnedBy(lease.LockId))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }
            if (operation.Type != PaymentProviderOperationType.RefundPayment
                || !operation.BusinessReferenceId.HasValue)
            {
                throw new InvalidOperationException($"Provider operation {operation.Id} is not a refund operation.");
            }

            var payment = await db.LockPaymentAsync(operation.PaymentId, cancellationToken);
            var refund = await db.LockRefundAsync(operation.BusinessReferenceId.Value, cancellationToken);
            var previousStatus = payment.Status;
            refund.MarkSucceeded(result.ProviderRefundId, now);
            payment.CompleteRefund(refund.Amount);
            operation.Complete(lease.LockId, now);
            db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                payment.Id,
                previousStatus,
                payment.Status,
                "ProviderPaymentRefunded",
                now,
                $"Refund {refund.Id} completed."));
            var integrationEvent = new PaymentRefundedIntegrationEvent
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                RefundId = refund.Id,
                RefundRequestId = refund.RefundRequestId
            };
            outbox.Add(OutboxMessage.Create(
                integrationEvent.GetType().AssemblyQualifiedName!,
                JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
                now));
            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> CancelAsync(
        PaymentProviderOperationLease lease,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operation = await db.LockProviderOperationAsync(lease.OperationId, cancellationToken);
            if (operation is null || !operation.IsOwnedBy(lease.LockId))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            operation.Cancel(lease.LockId, reason, now);
            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> RecordFailureAsync(
        PaymentProviderOperationLease lease,
        Exception exception,
        DateTimeOffset nextAttemptOnUtc,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var operation = await db.LockProviderOperationAsync(lease.OperationId, cancellationToken);
            if (operation is null || !operation.IsOwnedBy(lease.LockId))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            var exhausted = operation.RecordFailure(
                lease.LockId,
                exception,
                nextAttemptOnUtc,
                maxAttempts,
                now);
            if (exhausted)
            {
                var payment = await db.LockPaymentAsync(operation.PaymentId, cancellationToken);
                switch (operation.Type)
                {
                    case PaymentProviderOperationType.CreateNativePayment:
                        RecordCreateFailure(payment, now);
                        break;
                    case PaymentProviderOperationType.CancelPayment:
                        db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                            payment.Id,
                            payment.Status,
                            payment.Status,
                            "PaymentCancellationExhausted",
                            now,
                            "Payment provider cancellation exhausted its retry policy."));
                        break;
                    case PaymentProviderOperationType.RefundPayment:
                        await RecordRefundFailureAsync(operation, payment, now, cancellationToken);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation.Type));
                }
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private void RecordCreateFailure(Payment payment, DateTimeOffset now)
    {
        const string failureReason = "Payment provider initialization failed.";
        var previousStatus = payment.Status;
        if (!payment.Fail(failureReason))
            return;

        db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
            payment.Id,
            previousStatus,
            payment.Status,
            "ProviderOperationExhausted",
            now,
            failureReason));
        var integrationEvent = new PaymentFailedIntegrationEvent
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Reason = failureReason
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
    }

    private async Task RecordRefundFailureAsync(
        PaymentProviderOperation operation,
        Payment payment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!operation.BusinessReferenceId.HasValue)
            throw new InvalidOperationException($"Refund operation {operation.Id} has no refund reference.");

        const string failureReason = "Payment refund exhausted its provider retry policy.";
        var refund = await db.LockRefundAsync(operation.BusinessReferenceId.Value, cancellationToken);
        var previousStatus = payment.Status;
        refund.MarkFailed(failureReason, now);
        payment.FailRefund(failureReason);
        db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
            payment.Id,
            previousStatus,
            payment.Status,
            "PaymentRefundFailed",
            now,
            failureReason));
        var integrationEvent = new PaymentRefundFailedIntegrationEvent
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            RefundId = refund.Id,
            RefundRequestId = refund.RefundRequestId,
            Reason = failureReason
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            now));
    }
}
