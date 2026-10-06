using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSA.CertificateService.Acme;

/// <summary>
/// ACME（RFC 8555）协议客户端，只实现签发证书用到的部分：账户、订单、验证、签发、下载。
/// 账户密钥固定 ECDSA P-256（JWS ES256）。没有用 Certes 之类的库：协议本身不大，
/// 而那些库会带进 BouncyCastle / Newtonsoft 等额外依赖。
/// </summary>
internal sealed class AcmeClient(HttpClient http, Uri directoryUrl, ECDsa accountKey)
{
    private const string BadNonce = "urn:ietf:params:acme:error:badNonce";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(3);

    private AcmeDirectory? _directory;
    private string? _nonce;

    /// <summary>账户 URL（JWS 的 kid）。新账户由 <see cref="RegisterAccountAsync"/> 设置。</summary>
    public string? AccountUrl { get; set; }

    /// <summary>注册账户；账户密钥已注册过时 CA 返回原账户。返回账户 URL。</summary>
    public async Task<string> RegisterAccountAsync(string contactEmail, CancellationToken cancellationToken)
    {
        var directory = await GetDirectoryAsync(cancellationToken);
        using var response = await PostAsync(directory.NewAccount, new
        {
            termsOfServiceAgreed = true,
            contact = new[] { $"mailto:{contactEmail}" }
        }, cancellationToken, useJwk: true);
        AccountUrl = response.Headers.Location?.ToString()
            ?? throw new AcmeException("The ACME server did not return an account URL.");
        return AccountUrl;
    }

    public async Task<(string Url, AcmeOrder Order)> CreateOrderAsync(IEnumerable<string> domains, CancellationToken cancellationToken)
    {
        var directory = await GetDirectoryAsync(cancellationToken);
        using var response = await PostAsync(directory.NewOrder, new
        {
            identifiers = domains.Select(domain => new { type = "dns", value = domain }).ToArray()
        }, cancellationToken);
        var url = response.Headers.Location?.ToString()
            ?? throw new AcmeException("The ACME server did not return an order URL.");
        return (url, await ReadAsync<AcmeOrder>(response, cancellationToken));
    }

    public async Task<AcmeAuthorization> GetAuthorizationAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(url, payload: null, cancellationToken);
        return await ReadAsync<AcmeAuthorization>(response, cancellationToken);
    }

    /// <summary>告诉 CA 验证记录 / 文件已就绪，可以来验证了。</summary>
    public async Task AnswerChallengeAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(url, new { }, cancellationToken);
    }

    /// <summary>等到授权不再是 pending；不是 valid 时抛出 CA 给的失败原因。</summary>
    public async Task WaitForAuthorizationAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            using var response = await PostAsync(url, payload: null, cancellationToken);
            var authorization = await ReadAsync<AcmeAuthorization>(response, cancellationToken);
            switch (authorization.Status)
            {
                case "valid":
                    return;
                case "pending":
                    if (DateTimeOffset.UtcNow > deadline)
                        throw new AcmeException($"Validation of {authorization.Identifier.Value} timed out.");
                    await Task.Delay(RetryDelay(response), cancellationToken);
                    break;
                default:
                    var problem = authorization.Challenges.Select(challenge => challenge.Error).FirstOrDefault(error => error is not null);
                    throw new AcmeException($"Validation of {authorization.Identifier.Value} failed: {problem?.Describe() ?? authorization.Status}");
            }
        }
    }

    /// <summary>提交 CSR 并等订单签发完成，返回证书下载地址。</summary>
    public async Task<string> FinalizeAsync(string orderUrl, byte[] csr, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var order = await WaitForOrderAsync(orderUrl, "ready", timeout, cancellationToken);
        if (order.Status == "ready")
        {
            using var response = await PostAsync(order.Finalize, new { csr = Base64Url.EncodeToString(csr) }, cancellationToken);
            order = await ReadAsync<AcmeOrder>(response, cancellationToken);
        }

        if (order.Status != "valid")
            order = await WaitForOrderAsync(orderUrl, "valid", timeout, cancellationToken);

        return order.Certificate ?? throw new AcmeException("The ACME order is valid but has no certificate URL.");
    }

    /// <summary>下载证书链 PEM（叶子在前）。</summary>
    public async Task<string> DownloadCertificateAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await PostAsync(url, payload: null, cancellationToken, accept: "application/pem-certificate-chain");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>key authorization = token "." base64url(账户公钥的 JWK 指纹)（RFC 8555 §8.1）。</summary>
    public string KeyAuthorization(string token) => $"{token}.{JwkThumbprint(accountKey)}";

    /// <summary>DNS-01 的 TXT 记录值：base64url(SHA-256(key authorization))。</summary>
    public static string DnsTxtValue(string keyAuthorization) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(keyAuthorization)));

    /// <summary>RFC 7638 JWK 指纹：按字段名排序、无空白的 JSON 再做 SHA-256。</summary>
    public static string JwkThumbprint(ECDsa key)
    {
        var (x, y) = Coordinates(key);
        var canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private async Task<AcmeOrder> WaitForOrderAsync(string orderUrl, string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            using var response = await PostAsync(orderUrl, payload: null, cancellationToken);
            var order = await ReadAsync<AcmeOrder>(response, cancellationToken);
            if (order.Status == "invalid")
                throw new AcmeException($"The ACME order became invalid: {order.Error?.Describe() ?? "no detail"}");
            if (order.Status == target || order.Status == "valid")
                return order;
            if (DateTimeOffset.UtcNow > deadline)
                throw new AcmeException($"The ACME order stayed '{order.Status}' for too long.");
            await Task.Delay(RetryDelay(response), cancellationToken);
        }
    }

    private async Task<AcmeDirectory> GetDirectoryAsync(CancellationToken cancellationToken) =>
        _directory ??= await http.GetFromJsonAsync<AcmeDirectory>(directoryUrl, Json, cancellationToken)
            ?? throw new AcmeException("The ACME directory is empty.");

    private async Task<string> NewNonceAsync(CancellationToken cancellationToken)
    {
        var directory = await GetDirectoryAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Head, directory.NewNonce);
        using var response = await http.SendAsync(request, cancellationToken);
        return ReplayNonce(response) ?? throw new AcmeException("The ACME server did not return a nonce.");
    }

    /// <summary>
    /// 发一个 JWS 签名的 POST（payload 为 null 即 POST-as-GET）。nonce 每次用完即换，
    /// 遇到 badNonce 用响应里带回的新 nonce 重试一次（RFC 8555 §6.5）。
    /// </summary>
    private async Task<HttpResponseMessage> PostAsync(
        string url,
        object? payload,
        CancellationToken cancellationToken,
        bool useJwk = false,
        string accept = "application/json")
    {
        for (var attempt = 0; ; attempt++)
        {
            var nonce = _nonce ?? await NewNonceAsync(cancellationToken);
            _nonce = null;

            object header = useJwk
                ? new { alg = "ES256", jwk = Jwk(accountKey), nonce, url }
                : new { alg = "ES256", kid = AccountUrl ?? throw new InvalidOperationException("Register the ACME account first."), nonce, url };
            var protectedPart = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header, Json));
            var payloadPart = payload is null ? string.Empty : Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
            var signature = accountKey.SignData(Encoding.ASCII.GetBytes($"{protectedPart}.{payloadPart}"), HashAlgorithmName.SHA256);

            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                @protected = protectedPart,
                payload = payloadPart,
                signature = Base64Url.EncodeToString(signature)
            }, Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/jose+json") } }
            };
            request.Headers.Accept.ParseAdd(accept);

            var response = await http.SendAsync(request, cancellationToken);
            _nonce = ReplayNonce(response);
            if (response.IsSuccessStatusCode)
                return response;

            using (response)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                if (problem?.Type == BadNonce && attempt == 0)
                    continue;
                throw new AcmeException($"ACME request to {url} failed ({(int)response.StatusCode}): {problem?.Describe() ?? response.ReasonPhrase}");
            }
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
        ?? throw new AcmeException($"The ACME server returned an empty {typeof(T).Name}.");

    private static async Task<AcmeProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AcmeProblem>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReplayNonce(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Replay-Nonce", out var values) ? values.FirstOrDefault() : null;

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        return retryAfter is { } delay ? TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 30)) : PollDelay;
    }

    private static object Jwk(ECDsa key)
    {
        var (x, y) = Coordinates(key);
        return new { crv = "P-256", kty = "EC", x, y };
    }

    private static (string X, string Y) Coordinates(ECDsa key)
    {
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return (Base64Url.EncodeToString(point.X), Base64Url.EncodeToString(point.Y));
    }
}

internal sealed record AcmeDirectory(string NewNonce, string NewAccount, string NewOrder);

internal sealed record AcmeOrder(string Status, string[] Authorizations, string Finalize, string? Certificate, AcmeProblem? Error);

internal sealed record AcmeIdentifier(string Type, string Value);

internal sealed record AcmeAuthorization(AcmeIdentifier Identifier, string Status, AcmeChallenge[] Challenges, bool? Wildcard);

internal sealed record AcmeChallenge(string Type, string Url, string Token, string Status, AcmeProblem? Error);

internal sealed record AcmeProblem(string? Type, string? Detail, AcmeProblem[]? Subproblems)
{
    public string Describe()
    {
        var text = $"{Type?.Replace("urn:ietf:params:acme:error:", string.Empty, StringComparison.Ordinal)}: {Detail}";
        return Subproblems is { Length: > 0 }
            ? $"{text} ({string.Join("; ", Subproblems.Select(problem => problem.Describe()))})"
            : text;
    }
}

public sealed class AcmeException(string message) : Exception(message);
