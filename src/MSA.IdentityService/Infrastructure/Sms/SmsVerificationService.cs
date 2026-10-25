using System.Globalization;
using MSA.Persistence.Postgres;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MSA.IdentityService.Application.Sms;
using MSA.IdentityService.Domain;

namespace MSA.IdentityService.Infrastructure.Sms;

internal sealed class SmsVerificationService : ISmsVerificationService
{
    private const string ProtectorPurpose = "MSA.IdentityService.SmsVerificationCode.v1";
    private readonly ApplicationDbContext _db;
    private readonly SmsOptions _options;
    private readonly IReadOnlyDictionary<string, ISmsSender> _senders;
    private readonly IDataProtector _protector;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SmsVerificationService> _logger;

    public SmsVerificationService(
        ApplicationDbContext db,
        IOptions<SmsOptions> options,
        IEnumerable<ISmsSender> senders,
        IDataProtectionProvider dataProtectionProvider,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        ILogger<SmsVerificationService> logger)
    {
        _db = db;
        _options = options.Value;
        _senders = senders
            .Where(sender => !string.IsNullOrWhiteSpace(sender.ProviderName))
            .ToDictionary(sender => sender.ProviderName.Trim(), StringComparer.OrdinalIgnoreCase);
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _environment = environment;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task RequestLoginCodeAsync(string phoneNumber, CancellationToken cancellationToken) =>
        RequestCodeAsync(
            phoneNumber,
            SmsVerificationPurpose.Login,
            _options.LoginTemplate,
            requiredUserId: null,
            cancellationToken);

    public async Task<bool> ConsumeLoginCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken) =>
        await ConsumeCodeAsync(phoneNumber, code, SmsVerificationPurpose.Login, cancellationToken) is not null;

    public Task RequestPasswordResetCodeAsync(string phoneNumber, CancellationToken cancellationToken) =>
        RequestCodeAsync(
            phoneNumber,
            SmsVerificationPurpose.PasswordReset,
            _options.PasswordResetTemplate,
            requiredUserId: null,
            cancellationToken);

    public async Task<string?> ConsumePasswordResetCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken) =>
        (await ConsumeCodeAsync(phoneNumber, code, SmsVerificationPurpose.PasswordReset, cancellationToken))?.UserId;

    public Task RequestPhoneConfirmationCodeAsync(
        string userId,
        string phoneNumber,
        CancellationToken cancellationToken) =>
        RequestCodeAsync(
            phoneNumber,
            SmsVerificationPurpose.PhoneConfirmation,
            _options.PhoneConfirmationTemplate,
            userId,
            cancellationToken);

    public async Task<string?> ConsumePhoneConfirmationCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken) =>
        (await ConsumeCodeAsync(phoneNumber, code, SmsVerificationPurpose.PhoneConfirmation, cancellationToken))?.UserId;

    private async Task RequestCodeAsync(
        string phoneNumber,
        SmsVerificationPurpose purpose,
        string template,
        string? requiredUserId,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.Normalize(phoneNumber);
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquirePhoneLockAsync(phone, cancellationToken);

        var userId = await _db.Users
            .AsNoTracking()
            .Where(user => user.PhoneNumber == phone && user.IsActive &&
                (requiredUserId == null
                    ? user.PhoneNumberConfirmed
                    : user.Id == requiredUserId && !user.PhoneNumberConfirmed))
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        // 登录即注册：没绑定账户的号码也发登录码，验证通过后由 AuthController 建号。
        if (userId is null && purpose != SmsVerificationPurpose.Login)
        {
            // 对外响应不变（防手机号枚举），只在服务端记录原因。
            _logger.LogInformation(
                "SMS {Purpose} code skipped for {Phone}: no active account with a matching {State} phone number.",
                purpose,
                MaskPhone(phone),
                requiredUserId == null ? "confirmed" : "unconfirmed");
            return;
        }

        var sentSince = now.AddHours(-1);
        var recentSendCount = await _db.SmsMessages.CountAsync(
            message => message.PhoneNumber == phone
                && message.Purpose == purpose
                && message.CreatedOnUtc >= sentSince,
            cancellationToken);
        if (recentSendCount >= _options.MaxSendsPerHour)
        {
            _logger.LogInformation(
                "SMS {Purpose} code skipped for {Phone}: hourly limit {Limit} reached.",
                purpose,
                MaskPhone(phone),
                _options.MaxSendsPerHour);
            return;
        }

        var activeMessages = await _db.SmsMessages
            .Where(message => message.PhoneNumber == phone
                && message.Purpose == purpose
                && (message.Status == SmsMessageStatus.Pending || message.Status == SmsMessageStatus.Sent)
                && message.ExpiresOnUtc > now)
            .OrderByDescending(message => message.CreatedOnUtc)
            .ToListAsync(cancellationToken);
        if (activeMessages.FirstOrDefault() is { } latest
            && latest.CreatedOnUtc > now.AddSeconds(-_options.ResendCooldownSeconds))
        {
            _logger.LogInformation(
                "SMS {Purpose} code skipped for {Phone}: still in the {Cooldown}s resend cooldown.",
                purpose,
                MaskPhone(phone),
                _options.ResendCooldownSeconds);
            return;
        }

        foreach (var activeMessage in activeMessages)
            activeMessage.Supersede(now);

        var code = _environment.IsDevelopment()
            ? _options.DevelopmentCode
            : RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var messageId = Guid.NewGuid();
        var provider = _environment.IsDevelopment()
            ? "development"
            : string.IsNullOrWhiteSpace(_options.Provider) ? "unconfigured" : _options.Provider.Trim();
        var message = new SmsVerificationMessage(
            messageId,
            userId,
            phone,
            purpose,
            template,
            _protector.Protect(BuildProtectedPayload(messageId, phone, code)),
            provider,
            now,
            now.AddSeconds(_options.CodeLifetimeSeconds));

        _db.SmsMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DispatchAsync(message, code, cancellationToken);
    }

    /// <summary>核验并消费验证码，成功时返回那条短信记录（登录码的 UserId 可能为空：号码当时还没注册）。</summary>
    private async Task<SmsVerificationMessage?> ConsumeCodeAsync(
        string phoneNumber,
        string code,
        SmsVerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.Normalize(phoneNumber);
        var normalizedCode = code.Trim();
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquirePhoneLockAsync(phone, cancellationToken);

        var message = await _db.SmsMessages
            .Where(candidate => candidate.PhoneNumber == phone
                && candidate.Purpose == purpose
                && candidate.Status == SmsMessageStatus.Sent)
            .OrderByDescending(candidate => candidate.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (message is null || message.ExpiresOnUtc <= now
            || message.UserId is null && purpose != SmsVerificationPurpose.Login)
            return null;

        if (!Matches(message, phone, normalizedCode))
        {
            message.RegisterFailedVerification(_options.MaxVerificationAttempts, now);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        message.Consume(now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    private async Task DispatchAsync(
        SmsVerificationMessage message,
        string code,
        CancellationToken cancellationToken)
    {
        if (_environment.IsDevelopment())
        {
            if (message.TryMarkSent(null, _timeProvider.GetUtcNow()))
                await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!_senders.TryGetValue(_options.Provider.Trim(), out var sender))
        {
            await MarkDeliveryFailedAsync(message, "provider_not_configured", cancellationToken);
            _logger.LogError(
                "SMS message {MessageId} was not sent because provider {Provider} is not registered.",
                message.Id,
                message.Provider);
            return;
        }

        try
        {
            var receipt = await sender.SendAsync(
                new SmsSendRequest(
                    message.PhoneNumber,
                    message.TemplateKey,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["code"] = code,
                        ["expiresInMinutes"] = Math.Ceiling(_options.CodeLifetimeSeconds / 60d)
                            .ToString(CultureInfo.InvariantCulture)
                    },
                    message.Id.ToString("N")),
                cancellationToken);

            if (message.TryMarkSent(receipt.ProviderMessageId, _timeProvider.GetUtcNow()))
                await _db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkDeliveryFailedAsync(message, "provider_error", cancellationToken);
            _logger.LogError(
                exception,
                "SMS provider {Provider} failed to send message {MessageId}.",
                message.Provider,
                message.Id);
        }
    }

    private async Task MarkDeliveryFailedAsync(
        SmsVerificationMessage message,
        string failureCode,
        CancellationToken cancellationToken)
    {
        if (message.TryMarkDeliveryFailed(failureCode, _timeProvider.GetUtcNow()))
            await _db.SaveChangesAsync(cancellationToken);
    }

    private bool Matches(SmsVerificationMessage message, string phoneNumber, string code)
    {
        string protectedPayload;
        try
        {
            protectedPayload = _protector.Unprotect(message.ProtectedCode);
        }
        catch (CryptographicException exception)
        {
            _logger.LogError(exception, "SMS verification payload {MessageId} could not be decrypted.", message.Id);
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(BuildProtectedPayload(message.Id, phoneNumber, code));
        var actual = Encoding.UTF8.GetBytes(protectedPayload);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private Task AcquirePhoneLockAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return _db.AcquireTransactionLockAsync($"identity:sms:{phoneNumber}", cancellationToken);
    }

    private static string MaskPhone(string phoneNumber) =>
        phoneNumber.Length > 7 ? $"{phoneNumber[..^8]}****{phoneNumber[^4..]}" : "****";

    private static string BuildProtectedPayload(Guid messageId, string phoneNumber, string code) =>
        $"{messageId:N}:{phoneNumber}:{code}";
}
