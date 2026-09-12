using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MSA.IdentityService.Application.Sms;

/// <summary>短信云服务商适配器。</summary>
public interface ISmsSender
{
    /// <summary>配置中用于选择适配器的唯一名称。</summary>
    string ProviderName { get; }

    /// <summary>使用服务商模板发送短信。</summary>
    Task<SmsSendReceipt> SendAsync(SmsSendRequest request, CancellationToken cancellationToken);
}

/// <summary>与具体云厂商无关的模板短信请求。</summary>
public sealed record SmsSendRequest(
    string PhoneNumber,
    string TemplateKey,
    IReadOnlyDictionary<string, string> Parameters,
    string IdempotencyKey);

/// <summary>短信云服务商接受消息后的回执。</summary>
public sealed record SmsSendReceipt(string? ProviderMessageId);

/// <summary>短信逻辑模板名称，由各云服务商适配器映射到实际模板 ID。</summary>
public static class SmsTemplateNames
{
    public const string LoginCode = "login-code";
}

public interface ISmsVerificationService
{
    Task RequestLoginCodeAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<string?> ConsumeLoginCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken);
}

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; init; } = string.Empty;
    public string LoginTemplate { get; init; } = SmsTemplateNames.LoginCode;
    public int CodeLifetimeSeconds { get; init; } = 300;
    public int ResendCooldownSeconds { get; init; } = 60;
    public int MaxSendsPerHour { get; init; } = 5;
    public int MaxVerificationAttempts { get; init; } = 5;
    public string DevelopmentCode { get; init; } = string.Empty;
}

internal static class PhoneNumberFormat
{
    internal const string E164Pattern = @"^\+[1-9][0-9]{7,14}$";
    internal const string E164Error = "手机号必须使用 E.164 格式，例如 +8613800138000。";

    internal static string Normalize(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        var normalized = phoneNumber.Trim();
        if (!Regex.IsMatch(normalized, E164Pattern, RegexOptions.CultureInvariant))
            throw new ArgumentException(E164Error, nameof(phoneNumber));
        return normalized;
    }
}

/// <summary>请求发送短信登录验证码。</summary>
public sealed record RequestSmsLoginCodeRequest(
    [Required, StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string PhoneNumber);

/// <summary>短信验证码发送请求的通用响应，不透露手机号是否已绑定账户。</summary>
public sealed record SmsLoginCodeRequestedResponse(int ExpiresInSeconds, int RetryAfterSeconds);

/// <summary>使用手机号和短信验证码登录。</summary>
public sealed record SmsLoginRequest(
    [Required, StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string PhoneNumber,
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^[0-9]{6}$")]
    string Code,
    bool RememberMe = false);
