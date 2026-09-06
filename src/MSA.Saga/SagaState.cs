using System.ComponentModel.DataAnnotations;
namespace MSA.Saga;

public sealed class SagaState
{
    private SagaState() { }
    public Guid CorrelationId { get; private set; }
    public string SagaType { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Data { get; private set; } = "{}";
    public SagaStatus Status { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? TimeoutOnUtc { get; private set; }
    public DateTimeOffset? TimeoutLockedUntilUtc { get; private set; }
    public Guid? TimeoutLockId { get; private set; }
    public string? FailureReason { get; private set; }
    [ConcurrencyCheck] public long Version { get; private set; }
    public static SagaState Start(Guid correlationId, string sagaType, string state, string data, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaType); ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return new() { CorrelationId = correlationId, SagaType = sagaType, State = state, Data = data, Status = SagaStatus.Running, CreatedOnUtc = now, UpdatedOnUtc = now };
    }
    public void TransitionTo(string state, string data, DateTimeOffset now) { EnsureActive(); State = state; Data = data; UpdatedOnUtc = now; Version++; }
    public void ScheduleTimeout(DateTimeOffset dueOnUtc, DateTimeOffset now) { EnsureActive(); TimeoutOnUtc = dueOnUtc; TimeoutLockId = null; TimeoutLockedUntilUtc = null; UpdatedOnUtc = now; Version++; }
    public void ClaimTimeout(Guid lockId, DateTimeOffset until, DateTimeOffset now) { TimeoutLockId = lockId; TimeoutLockedUntilUtc = until; UpdatedOnUtc = now; Version++; }
    public void ClearTimeout(Guid lockId, DateTimeOffset now) { EnsureTimeoutLock(lockId); TimeoutOnUtc = null; TimeoutLockId = null; TimeoutLockedUntilUtc = null; UpdatedOnUtc = now; Version++; }
    public void Complete(DateTimeOffset now) { EnsureActive(); Status = SagaStatus.Completed; TimeoutOnUtc = null; TimeoutLockId = null; TimeoutLockedUntilUtc = null; UpdatedOnUtc = now; Version++; }
    public void BeginCompensation(string state, string data, DateTimeOffset now)
    {
        // 补偿完成后仍可能观察到不可逆的迟到事件（例如延迟支付回调）；此时重新打开补偿流程，
        // 以撤销新发现的副作用。正常完成的 Saga 仍保持不可变。
        if (Status == SagaStatus.Completed)
            throw new InvalidOperationException($"Saga {CorrelationId} is terminal.");
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        State = state;
        Data = data;
        Status = SagaStatus.Compensating;
        FailureReason = null;
        TimeoutOnUtc = null;
        TimeoutLockId = null;
        TimeoutLockedUntilUtc = null;
        UpdatedOnUtc = now;
        Version++;
    }
    public void Compensate(DateTimeOffset now)
    {
        if (Status != SagaStatus.Compensating)
            throw new InvalidOperationException($"Saga {CorrelationId} is not compensating.");
        Status = SagaStatus.Compensated;
        TimeoutOnUtc = null;
        TimeoutLockId = null;
        TimeoutLockedUntilUtc = null;
        UpdatedOnUtc = now;
        Version++;
    }
    public void Fail(string reason, DateTimeOffset now) { if (Status is SagaStatus.Completed or SagaStatus.Compensated) throw new InvalidOperationException($"Saga {CorrelationId} is terminal."); Status = SagaStatus.Failed; FailureReason = reason; TimeoutOnUtc = null; TimeoutLockId = null; TimeoutLockedUntilUtc = null; UpdatedOnUtc = now; Version++; }
    private void EnsureActive() { if (Status is SagaStatus.Completed or SagaStatus.Compensated or SagaStatus.Failed) throw new InvalidOperationException($"Saga {CorrelationId} is terminal."); }
    private void EnsureTimeoutLock(Guid lockId) { if (TimeoutLockId != lockId) throw new InvalidOperationException($"Saga {CorrelationId} timeout is not owned by lock {lockId}."); }
}
