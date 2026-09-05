using MSA.Infrastrucute.Entities;
namespace MSA.NotificationService.Domain;

public sealed class Notification : AggregateRoot<Guid>
{
    private Notification() { }
    public Notification(Guid id, string userId, string templateCode, string subject, string body, string deduplicationKey) { Id = id; UserId = userId; TemplateCode = templateCode; Subject = subject; Body = body; DeduplicationKey = deduplicationKey; Status = NotificationStatus.Pending; NextAttemptOnUtc = DateTimeOffset.UtcNow; CreatedOnUtc = DateTimeOffset.UtcNow; }
    public string UserId { get; private set; } = ""; public string TemplateCode { get; private set; } = ""; public string Subject { get; private set; } = ""; public string Body { get; private set; } = ""; public string DeduplicationKey { get; private set; } = ""; public NotificationStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset NextAttemptOnUtc { get; private set; }
    public DateTimeOffset? SentOnUtc { get; private set; }
    public string? LastError { get; private set; }
    public Guid? LockId { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public long Version { get; private set; }
    public bool TryClaim(Guid lockId, DateTimeOffset now, TimeSpan lease) { if (Status == NotificationStatus.Sent || Status == NotificationStatus.Failed || NextAttemptOnUtc > now || (Status == NotificationStatus.Sending && LockedUntilUtc > now)) return false; Status = NotificationStatus.Sending; LockId = lockId; LockedUntilUtc = now.Add(lease); Version++; return true; }
    public void Sent(Guid lockId) { EnsureLock(lockId); Status = NotificationStatus.Sent; SentOnUtc = DateTimeOffset.UtcNow; LockId = null; LockedUntilUtc = null; Version++; }
    public void Failed(Guid lockId, Exception e) { EnsureLock(lockId); RetryCount++; Status = RetryCount >= 10 ? NotificationStatus.Failed : NotificationStatus.Pending; NextAttemptOnUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(Math.Pow(2, RetryCount), 300)); LastError = e.Message.Length <= 2000 ? e.Message : e.Message[..2000]; LockId = null; LockedUntilUtc = null; Version++; }
    private void EnsureLock(Guid lockId) { if (LockId != lockId) throw new InvalidOperationException("Notification lease is not owned by this worker."); }
}
public sealed class NotificationTemplate : AggregateRoot<Guid> { private NotificationTemplate() { } public NotificationTemplate(Guid id, string code, string subject, string body) { Id = id; Code = code; Subject = subject; Body = body; } public string Code { get; private set; } = ""; public string Subject { get; private set; } = ""; public string Body { get; private set; } = ""; public bool IsActive { get; private set; } = true; }
public sealed class UserNotificationPreference : AggregateRoot<Guid> { private UserNotificationPreference() { } public UserNotificationPreference(Guid id, string userId, bool email, bool sms) { Id = id; UserId = userId; EmailEnabled = email; SmsEnabled = sms; } public string UserId { get; private set; } = ""; public bool EmailEnabled { get; private set; } public bool SmsEnabled { get; private set; } }
public enum NotificationStatus { Pending, Sending, Sent, Failed }
