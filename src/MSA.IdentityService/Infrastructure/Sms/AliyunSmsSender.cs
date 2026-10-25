using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MSA.IdentityService.Application.Sms;

namespace MSA.IdentityService.Infrastructure.Sms;

/// <summary>
/// 阿里云号码认证服务的「短信认证」（Dypnsapi 2017-05-25 SendSmsVerifyCode，RPC 风格签名 v1）。
/// 只能用控制台赠送的签名 + 赠送的模板，只支持国内号码。验证码由 <see cref="SmsVerificationService"/> 生成并自行核验，
/// 所以 TemplateParam 直接传明文码，不用阿里云的 ##code## 和 CheckSmsVerifyCode。
/// 密钥：MSA_SMS_ALIYUN_ACCESS_KEY_ID / MSA_SMS_ALIYUN_ACCESS_KEY_SECRET，RAM 用户只需要 dypns:SendSmsVerifyCode。
/// </summary>
internal sealed class AliyunSmsSender(HttpClient httpClient, IOptions<AliyunSmsOptions> options, TimeProvider time) : ISmsSender
{
    public const string Name = "aliyun";
    internal const string KeyIdVariable = "MSA_SMS_ALIYUN_ACCESS_KEY_ID";
    internal const string SecretVariable = "MSA_SMS_ALIYUN_ACCESS_KEY_SECRET";
    private const string ChinaPrefix = "+86";

    public string ProviderName => Name;

    internal static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyIdVariable)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretVariable));

    public async Task<SmsSendReceipt> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        if (!request.PhoneNumber.StartsWith(ChinaPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Aliyun SMS verification only supports +86 phone numbers.");

        var keyId = Environment.GetEnvironmentVariable(KeyIdVariable) ?? throw new InvalidOperationException($"{KeyIdVariable} is not set.");
        var secret = Environment.GetEnvironmentVariable(SecretVariable) ?? throw new InvalidOperationException($"{SecretVariable} is not set.");
        var settings = options.Value;

        var parameters = new Dictionary<string, string>
        {
            ["Action"] = "SendSmsVerifyCode",
            ["Format"] = "JSON",
            ["Version"] = "2017-05-25",
            ["AccessKeyId"] = keyId,
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureVersion"] = "1.0",
            ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
            ["Timestamp"] = time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["PhoneNumber"] = request.PhoneNumber[ChinaPrefix.Length..],
            ["SignName"] = settings.SignName,
            ["TemplateCode"] = settings.Templates[request.TemplateKey],
            // 赠送模板的变量固定是 ${code} 和 ${min}。
            ["TemplateParam"] = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["code"] = request.Parameters["code"],
                ["min"] = request.Parameters["expiresInMinutes"]
            }),
            ["OutId"] = request.IdempotencyKey
        };
        parameters["Signature"] = Sign(parameters, secret);

        var query = string.Join('&', parameters.Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}"));
        using var response = await httpClient.GetAsync($"?{query}", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // 失败时 HTTP 400 + Code（如 FREQUENCY_FAIL、BUSINESS_LIMIT_CONTROL），成功时 Code = OK。
        var code = body.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() : null;
        if (code != "OK")
        {
            var message = body.TryGetProperty("Message", out var messageElement) ? messageElement.GetString() : response.ReasonPhrase;
            throw new InvalidOperationException($"Aliyun SendSmsVerifyCode failed: {code} {message}");
        }

        return new SmsSendReceipt(
            body.TryGetProperty("Model", out var model) && model.TryGetProperty("BizId", out var bizId) ? bizId.GetString() : null);
    }

    /// <summary>与 CertificateService 的 AliyunDnsProvider.Sign 相同的 RPC 签名 v1。</summary>
    private static string Sign(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        var canonical = string.Join('&', parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}"));
        var stringToSign = $"GET&{Encode("/")}&{Encode(canonical)}";
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret + "&"), Encoding.UTF8.GetBytes(stringToSign)));
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);
}

internal sealed class AliyunSmsOptions
{
    public const string SectionName = "Sms:Aliyun";

    /// <summary>短信认证控制台赠送的签名名称。</summary>
    public string SignName { get; init; } = string.Empty;

    /// <summary>逻辑模板键（<see cref="SmsTemplateNames"/>）→ 赠送模板的 TemplateCode。</summary>
    public Dictionary<string, string> Templates { get; init; } = [];
}
