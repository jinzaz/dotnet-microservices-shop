using System.ComponentModel.DataAnnotations;
namespace MSA.EventBusOutbox;
public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }
    public DateTimeOffset NextAttemptOnUtc { get; private set; }
    public DateTimeOffset? ProcessedOnUtc { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public Guid? LockId { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    [ConcurrencyCheck] public long Version { get; private set; }
    public static OutboxMessage Create(string eventType, string payload, DateTimeOffset? occurredOnUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType); ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        var now = occurredOnUtc ?? DateTimeOffset.UtcNow;
        return new() { Id = Guid.NewGuid(), EventType = eventType, Payload = payload, OccurredOnUtc = now, NextAttemptOnUtc = now };
    }
    public void Claim(Guid lockId, DateTimeOffset until) { LockId = lockId; LockedUntilUtc = until; Status = OutboxStatus.Processing; Version++; }
    public void MarkPublished(Guid lockId, DateTimeOffset now) { EnsureLock(lockId); Status = OutboxStatus.Published; ProcessedOnUtc = now; LockId = null; LockedUntilUtc = null; LastError = null; Version++; }
    public void MarkFailed(Guid lockId, Exception exception, DateTimeOffset nextAttempt, int maxRetries)
    {
        EnsureLock(lockId); RetryCount++; var error = exception.ToString(); LastError = error[..Math.Min(error.Length, 4000)];
        Status = RetryCount >= maxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;
        NextAttemptOnUtc = nextAttempt; LockId = null; LockedUntilUtc = null; Version++;
    }
    private void EnsureLock(Guid lockId) { if (LockId != lockId) throw new InvalidOperationException($"Outbox message {Id} is not owned by lock {lockId}."); }
}
