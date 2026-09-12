using System.Globalization;
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

    public async Task RequestLoginCodeAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.Normalize(phoneNumber);
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquirePhoneLockAsync(phone, cancellationToken);

        var userId = await _db.Users
            .AsNoTracking()
            .Where(user => user.PhoneNumber == phone && user.PhoneNumberConfirmed && user.IsActive)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (userId is null)
            return;

        var sentSince = now.AddHours(-1);
        var recentSendCount = await _db.SmsMessages.CountAsync(
            message => message.PhoneNumber == phone
                && message.Purpose == SmsVerificationPurpose.Login
                && message.CreatedOnUtc >= sentSince,
            cancellationToken);
        if (recentSendCount >= _options.MaxSendsPerHour)
            return;

        var activeMessages = await _db.SmsMessages
            .Where(message => message.PhoneNumber == phone
                && message.Purpose == SmsVerificationPurpose.Login
                && (message.Status == SmsMessageStatus.Pending || message.Status == SmsMessageStatus.Sent)
                && message.ExpiresOnUtc > now)
            .OrderByDescending(message => message.CreatedOnUtc)
            .ToListAsync(cancellationToken);
        if (activeMessages.FirstOrDefault() is { } latest
            && latest.CreatedOnUtc > now.AddSeconds(-_options.ResendCooldownSeconds))
        {
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
            SmsVerificationPurpose.Login,
            _options.LoginTemplate,
            _protector.Protect(BuildProtectedPayload(messageId, phone, code)),
            provider,
            now,
            now.AddSeconds(_options.CodeLifetimeSeconds));

        _db.SmsMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await DispatchAsync(message, code, cancellationToken);
    }

    public async Task<string?> ConsumeLoginCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken)
    {
        var phone = PhoneNumberFormat.Normalize(phoneNumber);
        var normalizedCode = code.Trim();
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquirePhoneLockAsync(phone, cancellationToken);

        var message = await _db.SmsMessages
            .Where(candidate => candidate.PhoneNumber == phone
                && candidate.Purpose == SmsVerificationPurpose.Login
                && candidate.Status == SmsMessageStatus.Sent)
            .OrderByDescending(candidate => candidate.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (message is null || message.ExpiresOnUtc <= now || message.UserId is null)
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
        return message.UserId;
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
        var lockKey = $"identity:sms-login:{phoneNumber}";
        return _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private static string BuildProtectedPayload(Guid messageId, string phoneNumber, string code) =>
        $"{messageId:N}:{phoneNumber}:{code}";
}
