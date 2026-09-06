namespace MSA.PaymentService.Domain;

internal sealed class PaymentProviderOperation
{
    private PaymentProviderOperation()
    {
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public PaymentProviderOperationType Type { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? BusinessReferenceId { get; private set; }
    public string? Reason { get; private set; }
    public PaymentProviderOperationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptOnUtc { get; private set; }
    public Guid? LockId { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? CompletedOnUtc { get; private set; }
    public long Version { get; private set; }

    public static PaymentProviderOperation CreatePayment(
        Guid paymentId,
        string merchantTradeNo,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantTradeNo);
        return new PaymentProviderOperation
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            Type = PaymentProviderOperationType.CreateNativePayment,
            IdempotencyKey = $"create:{merchantTradeNo.Trim()}",
            Status = PaymentProviderOperationStatus.Pending,
            NextAttemptOnUtc = now,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };
    }

    public static PaymentProviderOperation CancelPayment(
        Guid paymentId,
        string merchantTradeNo,
        string reason,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantTradeNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PaymentProviderOperation
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            Type = PaymentProviderOperationType.CancelPayment,
            IdempotencyKey = $"cancel:{merchantTradeNo.Trim()}",
            Reason = reason[..Math.Min(reason.Length, 2000)],
            Status = PaymentProviderOperationStatus.Pending,
            NextAttemptOnUtc = now,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };
    }

    public static PaymentProviderOperation RefundPayment(
        Guid paymentId,
        Guid refundId,
        string merchantRefundNo,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantRefundNo);
        return new PaymentProviderOperation
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            Type = PaymentProviderOperationType.RefundPayment,
            IdempotencyKey = $"refund:{merchantRefundNo.Trim()}",
            BusinessReferenceId = refundId,
            Status = PaymentProviderOperationStatus.Pending,
            NextAttemptOnUtc = now,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };
    }

    public void Claim(Guid lockId, DateTimeOffset lockedUntilUtc, DateTimeOffset now)
    {
        // 只有待处理或租约过期的操作可被领取，防止多个后台实例同时调用外部支付渠道。
        if (Status != PaymentProviderOperationStatus.Pending
            && !(Status == PaymentProviderOperationStatus.Processing && LockedUntilUtc < now))
        {
            throw new InvalidOperationException($"Provider operation {Id} is not claimable.");
        }

        Status = PaymentProviderOperationStatus.Processing;
        AttemptCount++;
        LockId = lockId;
        LockedUntilUtc = lockedUntilUtc;
        UpdatedOnUtc = now;
        Version++;
    }

    public bool IsOwnedBy(Guid lockId) =>
        Status == PaymentProviderOperationStatus.Processing && LockId == lockId;

    public void Complete(Guid lockId, DateTimeOffset now)
    {
        EnsureOwned(lockId);
        Status = PaymentProviderOperationStatus.Completed;
        CompletedOnUtc = now;
        LastError = null;
        ClearLease();
        UpdatedOnUtc = now;
        Version++;
    }

    public bool RecordFailure(
        Guid lockId,
        Exception exception,
        DateTimeOffset nextAttemptOnUtc,
        int maxAttempts,
        DateTimeOffset now)
    {
        EnsureOwned(lockId);
        var error = exception.ToString();
        LastError = error[..Math.Min(error.Length, 4000)];
        var exhausted = AttemptCount >= maxAttempts;
        Status = exhausted
            ? PaymentProviderOperationStatus.Failed
            : PaymentProviderOperationStatus.Pending;
        NextAttemptOnUtc = nextAttemptOnUtc;
        ClearLease();
        UpdatedOnUtc = now;
        Version++;
        return exhausted;
    }

    public void Cancel(Guid lockId, string reason, DateTimeOffset now)
    {
        EnsureOwned(lockId);
        Status = PaymentProviderOperationStatus.Cancelled;
        LastError = reason[..Math.Min(reason.Length, 4000)];
        ClearLease();
        UpdatedOnUtc = now;
        Version++;
    }

    public bool Requeue(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is PaymentProviderOperationStatus.Pending
            or PaymentProviderOperationStatus.Processing)
        {
            return false;
        }
        if (Status == PaymentProviderOperationStatus.Completed)
            throw new InvalidOperationException($"Completed provider operation {Id} cannot be requeued.");

        var normalizedReason = reason.Trim();
        Reason = normalizedReason[..Math.Min(normalizedReason.Length, 2000)];
        Status = PaymentProviderOperationStatus.Pending;
        AttemptCount = 0;
        NextAttemptOnUtc = now;
        CompletedOnUtc = null;
        ClearLease();
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    private void EnsureOwned(Guid lockId)
    {
        if (!IsOwnedBy(lockId))
            throw new InvalidOperationException($"Provider operation {Id} is not owned by lease {lockId}.");
    }

    private void ClearLease()
    {
        LockId = null;
        LockedUntilUtc = null;
    }
}

internal enum PaymentProviderOperationType
{
    CreateNativePayment = 0,
    CancelPayment = 1,
    RefundPayment = 2
}

internal enum PaymentProviderOperationStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
