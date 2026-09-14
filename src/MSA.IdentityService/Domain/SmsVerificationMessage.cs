namespace MSA.IdentityService.Domain;

internal sealed class SmsVerificationMessage
{
    private SmsVerificationMessage()
    {
    }

    internal SmsVerificationMessage(
        Guid id,
        string userId,
        string phoneNumber,
        SmsVerificationPurpose purpose,
        string templateKey,
        string protectedCode,
        string provider,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc)
    {
        Id = id;
        UserId = userId;
        PhoneNumber = phoneNumber;
        Purpose = purpose;
        TemplateKey = templateKey;
        ProtectedCode = protectedCode;
        Provider = provider;
        Status = SmsMessageStatus.Pending;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;
    }

    public Guid Id { get; private set; }
    public string? UserId { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;
    public SmsVerificationPurpose Purpose { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public string ProtectedCode { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string? ProviderMessageId { get; private set; }
    public SmsMessageStatus Status { get; private set; }
    public int VerificationAttempts { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset ExpiresOnUtc { get; private set; }
    public DateTimeOffset? SentOnUtc { get; private set; }
    public DateTimeOffset? ConsumedOnUtc { get; private set; }
    public long Version { get; private set; }

    internal bool TryMarkSent(string? providerMessageId, DateTimeOffset now)
    {
        if (Status != SmsMessageStatus.Pending)
            return false;

        ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim();
        Status = SmsMessageStatus.Sent;
        SentOnUtc = now;
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    internal bool TryMarkDeliveryFailed(string failureCode, DateTimeOffset now)
    {
        if (Status != SmsMessageStatus.Pending)
            return false;

        FailureCode = failureCode;
        Status = SmsMessageStatus.Failed;
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    internal void Supersede(DateTimeOffset now)
    {
        if (Status is not (SmsMessageStatus.Pending or SmsMessageStatus.Sent))
            return;

        Status = SmsMessageStatus.Superseded;
        UpdatedOnUtc = now;
        Version++;
    }

    internal void RegisterFailedVerification(int maximumAttempts, DateTimeOffset now)
    {
        if (Status != SmsMessageStatus.Sent)
            return;

        VerificationAttempts++;
        if (VerificationAttempts >= maximumAttempts)
            Status = SmsMessageStatus.VerificationLocked;
        UpdatedOnUtc = now;
        Version++;
    }

    internal void Consume(DateTimeOffset now)
    {
        if (Status != SmsMessageStatus.Sent)
            throw new InvalidOperationException("Only a sent SMS verification message can be consumed.");

        Status = SmsMessageStatus.Consumed;
        ConsumedOnUtc = now;
        UpdatedOnUtc = now;
        Version++;
    }
}

internal enum SmsVerificationPurpose
{
    Login = 0,
    PasswordReset = 1,
    PhoneConfirmation = 2
}

internal enum SmsMessageStatus
{
    Pending = 0,
    Sent = 1,
    Consumed = 2,
    Superseded = 3,
    Failed = 4,
    VerificationLocked = 5
}
