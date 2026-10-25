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
    public const string PasswordResetCode = "password-reset-code";
    public const string PhoneConfirmationCode = "phone-confirmation-code";
}

public interface ISmsVerificationService
{
    /// <summary>未注册的号码也会发码（登录即注册）。</summary>
    Task RequestLoginCodeAsync(string phoneNumber, CancellationToken cancellationToken);
    /// <summary>只回答验证码对不对；登录哪个账户由调用方按"当前持有这个已验证号码的账户"决定。</summary>
    Task<bool> ConsumeLoginCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken);
    Task RequestPasswordResetCodeAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<string?> ConsumePasswordResetCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken);
    Task RequestPhoneConfirmationCodeAsync(
        string userId,
        string phoneNumber,
        CancellationToken cancellationToken);
    Task<string?> ConsumePhoneConfirmationCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken);
}

public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; init; } = string.Empty;
    public string LoginTemplate { get; init; } = SmsTemplateNames.LoginCode;
    public string PasswordResetTemplate { get; init; } = SmsTemplateNames.PasswordResetCode;
    public string PhoneConfirmationTemplate { get; init; } = SmsTemplateNames.PhoneConfirmationCode;
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

/// <summary>请求发送短信验证码（登录、找回密码），需要先答对图形验证码。</summary>
public sealed record RequestSmsLoginCodeRequest(
    [Required, StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string PhoneNumber,
    [Required, StringLength(32, MinimumLength = 32)]
    string CaptchaId,
    [Required, RegularExpression(@"^[0-9]{1,3}$", ErrorMessage = "请输入图形验证码的计算结果。")]
    string CaptchaAnswer);

/// <summary>短信验证码发送请求的通用响应，不透露手机号是否已绑定账户。</summary>
public sealed record SmsLoginCodeRequestedResponse(int ExpiresInSeconds, int RetryAfterSeconds);

/// <summary>使用手机号和短信验证码登录。</summary>
public sealed record SmsLoginRequest(
    [Required, StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string PhoneNumber,
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^[0-9]{6}$")]
    string Code,
    bool RememberMe = false);

/// <summary>使用已验证手机号收到的验证码重置密码。</summary>
public sealed record SmsPasswordResetRequest(
    [Required, StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string PhoneNumber,
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^[0-9]{6}$")]
    string Code,
    [Required, StringLength(128, MinimumLength = 10)]
    string NewPassword);
