using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSA.CertificateService.Acme;

/// <summary>
/// DNS-01 验证要在 _acme-challenge.&lt;域名&gt; 上临时加一条 TXT 记录。凭据只从环境变量读（同 MSA_CLIENT_SECRET_* 的做法），
/// 没配凭据的服务商在后台不可选。新增服务商实现这个接口并在 Program.cs 注册即可。
/// </summary>
public interface IDnsChallengeProvider
{
    /// <summary>订阅里保存的服务商代码。</summary>
    string Name { get; }

    string DisplayName { get; }

    bool IsConfigured { get; }

    /// <returns>删除这条记录要用的句柄。</returns>
    Task<string> AddTxtRecordAsync(string fqdn, string value, CancellationToken cancellationToken);

    Task RemoveTxtRecordAsync(string handle, CancellationToken cancellationToken);
}

/// <summary>Cloudflare：API Token 需要 Zone.DNS 编辑权限（MSA_ACME_CLOUDFLARE_API_TOKEN）。</summary>
internal sealed class CloudflareDnsProvider(IHttpClientFactory httpClientFactory) : IDnsChallengeProvider
{
    public const string HttpClientName = "dns-cloudflare";
    private const string TokenVariable = "MSA_ACME_CLOUDFLARE_API_TOKEN";

    public string Name => "cloudflare";
    public string DisplayName => "Cloudflare";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable));

    public async Task<string> AddTxtRecordAsync(string fqdn, string value, CancellationToken cancellationToken)
    {
        var http = CreateClient();
        var zoneId = await FindZoneAsync(http, fqdn, cancellationToken);
        using var response = await http.PostAsJsonAsync(
            $"zones/{zoneId}/dns_records",
            new { type = "TXT", name = fqdn, content = value, ttl = 60 },
            cancellationToken);
        var record = await ReadAsync<CloudflareRecord>(response, cancellationToken);
        return $"{zoneId}/{record.Id}";
    }

    public async Task RemoveTxtRecordAsync(string handle, CancellationToken cancellationToken)
    {
        var http = CreateClient();
        var (zoneId, recordId) = (handle[..handle.IndexOf('/', StringComparison.Ordinal)], handle[(handle.IndexOf('/', StringComparison.Ordinal) + 1)..]);
        using var response = await http.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}", cancellationToken);
        await ReadAsync<CloudflareRecord>(response, cancellationToken);
    }

    /// <summary>从最长的父域往上找，找到的第一个托管在这个账号下的 zone 就是记录所在的 zone。</summary>
    private static async Task<string> FindZoneAsync(HttpClient http, string fqdn, CancellationToken cancellationToken)
    {
        var labels = fqdn.TrimEnd('.').Split('.');
        for (var start = 1; start < labels.Length - 1; start++)
        {
            var candidate = string.Join('.', labels[start..]);
            using var response = await http.GetAsync($"zones?name={Uri.EscapeDataString(candidate)}", cancellationToken);
            var zones = await ReadAsync<CloudflareZone[]>(response, cancellationToken);
            if (zones.Length > 0)
                return zones[0].Id;
        }

        throw new AcmeException($"No Cloudflare zone hosts {fqdn}.");
    }

    private HttpClient CreateClient()
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Environment.GetEnvironmentVariable(TokenVariable) ?? throw new AcmeException($"{TokenVariable} is not set."));
        return http;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var envelope = await response.Content.ReadFromJsonAsync<CloudflareEnvelope<T>>(cancellationToken);
        if (envelope is { Success: true, Result: not null })
            return envelope.Result;

        var errors = envelope?.Errors?.Select(error => $"{error.Code} {error.Message}") ?? [response.ReasonPhrase ?? "unknown error"];
        throw new AcmeException($"Cloudflare API failed: {string.Join("; ", errors)}");
    }

    private sealed record CloudflareEnvelope<T>(bool Success, T? Result, CloudflareError[]? Errors);
    private sealed record CloudflareError(int Code, string Message);
    private sealed record CloudflareZone(string Id);
    private sealed record CloudflareRecord(string Id);
}

/// <summary>
/// 阿里云云解析（alidns，RPC 风格签名 v1）：MSA_ACME_ALIYUN_ACCESS_KEY_ID / MSA_ACME_ALIYUN_ACCESS_KEY_SECRET，
/// RAM 用户只需要 AliyunDNSFullAccess。
/// </summary>
internal sealed class AliyunDnsProvider(IHttpClientFactory httpClientFactory, TimeProvider time) : IDnsChallengeProvider
{
    public const string HttpClientName = "dns-aliyun";
    private const string KeyIdVariable = "MSA_ACME_ALIYUN_ACCESS_KEY_ID";
    private const string SecretVariable = "MSA_ACME_ALIYUN_ACCESS_KEY_SECRET";

    public string Name => "aliyun";
    public string DisplayName => "阿里云解析";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyIdVariable)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretVariable));

    public async Task<string> AddTxtRecordAsync(string fqdn, string value, CancellationToken cancellationToken)
    {
        // GetMainDomainName 把完整名拆成"主域名 + 主机记录"，不用自己猜哪一级是注册域名。
        var main = await CallAsync("GetMainDomainName", new() { ["InputString"] = fqdn.TrimEnd('.') }, cancellationToken);
        var added = await CallAsync("AddDomainRecord", new()
        {
            ["DomainName"] = main.GetProperty("DomainName").GetString()!,
            ["RR"] = main.GetProperty("RR").GetString()!,
            ["Type"] = "TXT",
            ["Value"] = value,
            ["TTL"] = "600" // 免费版云解析的最小 TTL
        }, cancellationToken);
        return added.GetProperty("RecordId").GetString()!;
    }

    public async Task RemoveTxtRecordAsync(string handle, CancellationToken cancellationToken) =>
        await CallAsync("DeleteDomainRecord", new() { ["RecordId"] = handle }, cancellationToken);

    private async Task<JsonElement> CallAsync(string action, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var keyId = Environment.GetEnvironmentVariable(KeyIdVariable) ?? throw new AcmeException($"{KeyIdVariable} is not set.");
        var secret = Environment.GetEnvironmentVariable(SecretVariable) ?? throw new AcmeException($"{SecretVariable} is not set.");

        parameters["Action"] = action;
        parameters["Format"] = "JSON";
        parameters["Version"] = "2015-01-09";
        parameters["AccessKeyId"] = keyId;
        parameters["SignatureMethod"] = "HMAC-SHA1";
        parameters["SignatureVersion"] = "1.0";
        parameters["SignatureNonce"] = Guid.NewGuid().ToString("N");
        parameters["Timestamp"] = time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        parameters["Signature"] = Sign(parameters, secret);

        var query = string.Join('&', parameters.Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}"));
        using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync($"?{query}", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (response.IsSuccessStatusCode)
            return body;

        var code = body.TryGetProperty("Code", out var codeElement) ? codeElement.GetString() : null;
        var message = body.TryGetProperty("Message", out var messageElement) ? messageElement.GetString() : response.ReasonPhrase;
        throw new AcmeException($"Aliyun DNS {action} failed: {code} {message}");
    }

    /// <summary>
    /// 阿里云 RPC 签名：参数按名字排序、逐个 RFC 3986 编码后用 &amp; 连接，再编码一次拼成
    /// "GET&amp;%2F&amp;..."，用 "AccessKeySecret&amp;" 做 HMAC-SHA1。
    /// </summary>
    internal static string Sign(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        var canonical = string.Join('&', parameters
            .Where(pair => pair.Key != "Signature")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}"));
        var stringToSign = $"GET&{Encode("/")}&{Encode(canonical)}";
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret + "&"), Encoding.UTF8.GetBytes(stringToSign)));
    }

    // Uri.EscapeDataString 只保留 RFC 3986 的非保留字符（含把 * 编成 %2A、空格编成 %20），正是阿里云要求的编码。
    private static string Encode(string value) => Uri.EscapeDataString(value);
}

/// <summary>
/// 加完 TXT 记录后先通过 DNS-over-HTTPS 确认它查得到，再让 CA 来验证，减少因为解析还没生效导致的失败。
/// 超时仍查不到也照样提交：CA 直接查权威服务器，公共解析器可能只是还有缓存。
/// </summary>
internal sealed partial class DnsPropagationChecker(
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<AcmeOptions> options,
    TimeProvider time,
    ILogger<DnsPropagationChecker> logger)
{
    public const string HttpClientName = "dns-over-https";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public async Task WaitAsync(IReadOnlyCollection<(string Name, string Value)> records, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + options.Value.DnsPropagationTimeout;
        var pending = records.ToList();
        while (true)
        {
            foreach (var record in pending.ToList())
            {
                if (await IsVisibleAsync(record.Name, record.Value, cancellationToken))
                    pending.Remove(record);
            }

            if (pending.Count == 0)
                return;
            if (time.GetUtcNow() >= deadline)
            {
                LogNotVisible(string.Join(", ", pending.Select(record => record.Name)));
                return;
            }

            await Task.Delay(Interval, time, cancellationToken);
        }
    }

    private async Task<bool> IsVisibleAsync(string name, string value, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.DohResolver}?name={Uri.EscapeDataString(name)}&type=16");
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            var answer = await response.Content.ReadFromJsonAsync<DohResponse>(cancellationToken);
            // TXT 的 data 带引号，长值会被拆成几段 "a" "b"。
            return answer?.Answer?.Any(record => record.Data.Replace("\"", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal) == value) == true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogResolverFailed(exception, name);
            return false;
        }
    }

    private sealed record DohResponse([property: JsonPropertyName("Answer")] DohAnswer[]? Answer);
    private sealed record DohAnswer([property: JsonPropertyName("data")] string Data);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TXT records {Names} are not visible through DNS-over-HTTPS yet; asking the CA to validate anyway.")]
    private partial void LogNotVisible(string names);

    [LoggerMessage(Level = LogLevel.Debug, Message = "DNS-over-HTTPS lookup of {Name} failed.")]
    private partial void LogResolverFailed(Exception exception, string name);
}
