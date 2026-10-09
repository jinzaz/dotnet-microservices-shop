using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace MSA.SearchService;

/// <summary>
/// 直接说 ES 的 REST/JSON，不引入官方客户端：这里只用到建索引、bulk、search、alias 几个固定接口，
/// 手写 JSON 和 ES 文档一一对应、好排查，也不会被客户端大版本的破坏性改动牵着走（8.x、9.x 都能用）。
/// <para>
/// 生产问题 #12：这是单例，所以每次调用都从 IHttpClientFactory 取客户端，而不是在单例里攥着一个 HttpClient——
/// 攥着不放的客户端拿不到 DNS 变化，ES 节点换 IP 后会一直连旧地址。
/// </para>
/// </summary>
internal sealed class ElasticsearchHttp(IHttpClientFactory factory)
{
    public const string ClientName = "elasticsearch";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient(ClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
                client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
                client.Timeout = options.RequestTimeout;
                client.DefaultRequestHeaders.Authorization = Authorization(options);
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
                var handler = new SocketsHttpHandler
                {
                    // 连接池定期换新连接，ES 节点扩缩容或换 IP 后能自然切过去。
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    AutomaticDecompression = DecompressionMethods.All
                };
                // 生产问题 #11：只在走 https 且配了 CA 时生效（明文 http 没有 TLS 握手，这里不起作用）。
                // ES 用自签 CA 时，把信任锚换成这张 CA，而不是关掉校验：主机名校验、中间证书、有效期仍由框架按标准流程做，
                // 只是「根」不再查操作系统的证书库。
                if (!string.IsNullOrWhiteSpace(options.CaCertificatePath))
                {
                    handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
                    {
                        TrustMode = X509ChainTrustMode.CustomRootTrust,
                        CustomTrustStore = { X509CertificateLoader.LoadCertificateFromFile(options.CaCertificatePath) },
                        // 自签 CA 没有吊销列表（CRL/OCSP）地址，查了只会失败；换证靠重新签发 + 重启。
                        RevocationMode = X509RevocationMode.NoCheck
                    };
                }

                return handler;
            });
    }

    private static AuthenticationHeaderValue? Authorization(ElasticsearchOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            return new AuthenticationHeaderValue("ApiKey", options.ApiKey);
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            return new AuthenticationHeaderValue("Basic", credentials);
        }

        return null;
    }

    public async Task<(HttpStatusCode Status, JsonNode? Body)> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken,
        params HttpStatusCode[] accepted)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");
        return await SendAsync(request, cancellationToken, accepted);
    }

    /// <summary>bulk 用 NDJSON：每行一个 JSON，最后必须以换行结尾。</summary>
    public async Task<JsonNode> BulkAsync(string path, string ndjson, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson")
        };
        var (_, body) = await SendAsync(request, cancellationToken);
        return body ?? throw new ElasticsearchException(HttpStatusCode.OK, "Bulk response had no body.");
    }

    private async Task<(HttpStatusCode Status, JsonNode? Body)> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        params HttpStatusCode[] accepted)
    {
        var client = factory.CreateClient(ClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        if (response.IsSuccessStatusCode || accepted.Contains(response.StatusCode))
            return (response.StatusCode, body);

        // 只带 ES 给出的错误类型与原因，不把整段响应（可能很长、含文档内容）写进日志。
        var error = body?["error"];
        var reason = error is JsonObject
            ? $"{error["type"]}: {error["reason"]}"
            : error?.ToString() ?? text[..Math.Min(text.Length, 300)];
        throw new ElasticsearchException(response.StatusCode, $"{request.Method} {request.RequestUri} → {(int)response.StatusCode} {reason}");
    }
}

internal sealed class ElasticsearchException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
