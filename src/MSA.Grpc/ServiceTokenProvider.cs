using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MSA.Grpc;

/// <summary>服务间调用的访问令牌来源。按 scope 组合区分：不同下游拿到的令牌受众不同。</summary>
public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default);
}

/// <summary>
/// OpenIddict client_credentials 流程的令牌提供者。令牌按 scope 组合缓存并在过期前提前刷新，
/// 每个 scope 组合同一时刻只会有一次取令牌请求，避免突发流量把 IdentityService 打满。
/// </summary>
internal sealed class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ServiceAuthenticationOptions> options,
    TimeProvider clock,
    ILogger<ClientCredentialsTokenProvider> logger) : IServiceTokenProvider
{
    public const string HttpClientName = "msa-service-token";

    private readonly ServiceAuthenticationOptions _options = options.Value;
    private readonly Dictionary<string, CachedToken> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public async Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var key = string.Join(' ', scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Order(StringComparer.Ordinal));
        if (TryGetValid(key, out var cached))
            return cached;

        var mutex = LockFor(key);
        await mutex.WaitAsync(cancellationToken);
        try
        {
            // 排队期间别人可能已经刷好了。
            if (TryGetValid(key, out cached))
                return cached;

            var token = await RequestTokenAsync(key, cancellationToken);
            lock (_gate)
                _cache[key] = token;
            return token.AccessToken;
        }
        finally
        {
            mutex.Release();
        }
    }

    private bool TryGetValid(string key, out string accessToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && cached.ExpiresOnUtc - _options.RefreshSkew > clock.GetUtcNow())
            {
                accessToken = cached.AccessToken;
                return true;
            }
        }

        accessToken = string.Empty;
        return false;
    }

    private SemaphoreSlim LockFor(string key)
    {
        lock (_gate)
        {
            if (!_locks.TryGetValue(key, out var mutex))
                _locks[key] = mutex = new SemaphoreSlim(1, 1);
            return mutex;
        }
    }

    private async Task<CachedToken> RequestTokenAsync(string scopes, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            // ValidateOnStart 已保证存在。
            ["client_secret"] = _options.ClientSecret!
        };
        if (!string.IsNullOrWhiteSpace(scopes))
            form["scope"] = scopes;

        using var response = await client.PostAsync(
            new Uri(new Uri(_options.Authority.TrimEnd('/') + "/"), "connect/token"),
            new FormUrlEncodedContent(form),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Service token request for scopes '{Scopes}' failed with {Status}: {Body}", scopes, response.StatusCode, body);
            throw new InvalidOperationException($"Could not obtain a service token for scopes '{scopes}' ({(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The token endpoint returned an empty response.");
        if (string.IsNullOrWhiteSpace(payload.AccessToken))
            throw new InvalidOperationException("The token endpoint returned no access token.");

        var lifetime = payload.ExpiresIn > 0 ? TimeSpan.FromSeconds(payload.ExpiresIn) : TimeSpan.FromMinutes(5);
        logger.LogDebug("Obtained a service token for scopes '{Scopes}', valid for {Lifetime}.", scopes, lifetime);
        return new CachedToken(payload.AccessToken, clock.GetUtcNow().Add(lifetime));
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}

/// <summary>把服务令牌挂到每个 gRPC 请求上；令牌过期由提供者负责，这里只管取用。</summary>
internal sealed class ServiceTokenHandler(IServiceTokenProvider tokens, string[] scopes) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(scopes, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
