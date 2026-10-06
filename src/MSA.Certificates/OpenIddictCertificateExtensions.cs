using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;
using OpenIddict.Server;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.Certificates;

/// <summary>
/// 让 OpenIddict 的签名 / 加密证书来自证书库，换证书不用重启：
/// <list type="bullet">
/// <item>证书库里本用途的可用证书（当前、待生效、退役中）全部注册进选项：JWKS 提前发布新密钥，旧令牌仍能验签 / 解密。</item>
/// <item>证书库变化或时间跨过激活点时，选项按变更令牌重建（OpenIddict 每个请求都读 IOptionsMonitor.CurrentValue）。</item>
/// <item>签发令牌时用哪张证书由证书库的"当前证书"决定，而不是 OpenIddict 自己按到期时间挑的那张。</item>
/// </list>
/// 证书库里还没有当前证书时：开发环境保留 AddDevelopment*Certificate 注册的开发证书，其余环境用环境变量里的 pfx。
/// </summary>
public static class OpenIddictCertificateExtensions
{
    /// <summary>认证中心：令牌签名（oidc-signing）与加密（oidc-encryption）证书。</summary>
    public static OpenIddictServerBuilder UseMsaCertificates(this OpenIddictServerBuilder builder)
    {
        var services = builder.Services;
        services.TryAddSingleton(CertificateStore.Disabled);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIddictServerOptions>, ServerCertificateSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<OpenIddictServerOptions>, CertificateChangeTokenSource<OpenIddictServerOptions>>());
        // UseLocalServer() 的验证端构建选项时把服务端密钥复制过去，要跟着重建，否则认证中心自己的 API 验不了新签的令牌。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<OpenIddictValidationOptions>, ServerDerivedChangeTokenSource>());
        services.AddHostedService<OptionsStartupCheck<OpenIddictServerOptions>>();

        builder.AddEventHandler<OpenIddictServerEvents.GenerateTokenContext>(handler => handler
            .UseSingletonHandler<ServerActiveCredentialSelector>()
            .SetOrder(OpenIddictServerHandlers.Protection.AttachSecurityCredentials.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom));
        return builder;
    }

    /// <summary>BFF 作为 OIDC 客户端：state token 的签名与加密都用 oidc-client 证书。</summary>
    public static OpenIddictClientBuilder UseMsaCertificates(this OpenIddictClientBuilder builder)
    {
        var services = builder.Services;
        services.TryAddSingleton(CertificateStore.Disabled);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIddictClientOptions>, ClientCertificateSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<OpenIddictClientOptions>, CertificateChangeTokenSource<OpenIddictClientOptions>>());
        services.AddHostedService<OptionsStartupCheck<OpenIddictClientOptions>>();

        builder.AddEventHandler<OpenIddictClientEvents.GenerateTokenContext>(handler => handler
            .UseSingletonHandler<ClientActiveCredentialSelector>()
            .SetOrder(OpenIddictClientHandlers.Protection.AttachSecurityCredentials.Descriptor.Order + 1)
            .SetType(OpenIddictClientHandlerType.Custom));
        return builder;
    }

    /// <summary>
    /// 证书库里本用途有当前证书时返回全部可用证书（当前证书在前）；否则开发环境返回 null（保留开发证书），
    /// 其余环境返回环境变量里的 pfx。
    /// </summary>
    internal static IReadOnlyList<X509Certificate2>? Resolve(CertificateStore store, string purpose, IHostEnvironment environment)
    {
        var stored = store.GetUsable(purpose);
        if (stored.Count > 0)
            return stored.Select(certificate => certificate.Certificate).ToList();

        return environment.IsDevelopment() ? null : [OpenIddictCertificateEnvironment.Load()];
    }

    // 跟 OpenIddict 的 AddSigningCertificate / AddEncryptionCertificate 对 RSA 证书的选择一致。
    internal static SigningCredentials Signing(X509Certificate2 certificate) =>
        new(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256);

    internal static EncryptingCredentials Encryption(X509Certificate2 certificate) =>
        new(new X509SecurityKey(certificate), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512);

    internal static bool IsCertificate(SecurityKey key, StoredCertificate? active) =>
        active is not null &&
        key is X509SecurityKey { Certificate: var certificate } &&
        certificate.Thumbprint == active.Certificate.Thumbprint;
}

/// <summary>
/// 启动时就构建一次选项：证书库里没有当前证书、环境变量 pfx 也没配时立刻起不来，而不是健康检查通过、
/// 等第一个登录请求才报错。放在 StartedAsync：那时证书库刷新器已经把证书读进来了，与注册顺序无关。
/// </summary>
internal sealed class OptionsStartupCheck<TOptions>(IOptionsMonitor<TOptions> options) : IHostedLifecycleService
    where TOptions : class
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _ = options.CurrentValue;
        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class CertificateChangeTokenSource<TOptions>(CertificateStore store) : IOptionsChangeTokenSource<TOptions>
{
    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => store.GetChangeToken();
}

/// <summary>
/// 验证端选项必须在服务端选项重建之后再重建：IOptionsMonitor 收到变更时会立刻重建并缓存，
/// 两者若挂在同一个证书库令牌上，回调先后不确定，验证端可能先重建、复制到旧的服务端密钥。
/// 所以改为跟在服务端选项的 OnChange 后面触发——那时服务端选项已经是新的。
/// </summary>
internal sealed class ServerDerivedChangeTokenSource : IOptionsChangeTokenSource<OpenIddictValidationOptions>, IDisposable
{
    private readonly IDisposable? _subscription;
    private CancellationTokenSource _changes = new();

    public ServerDerivedChangeTokenSource(IOptionsMonitor<OpenIddictServerOptions> server) =>
        _subscription = server.OnChange(_ => Interlocked.Exchange(ref _changes, new CancellationTokenSource()).Cancel());

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changes).Token);

    public void Dispose() => _subscription?.Dispose();
}

internal sealed class ServerCertificateSetup(CertificateStore store, IHostEnvironment environment) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        if (OpenIddictCertificateExtensions.Resolve(store, CertificatePurposes.OidcSigningCode, environment) is { } signing)
        {
            options.SigningCredentials.Clear();
            options.SigningCredentials.AddRange(signing.Select(OpenIddictCertificateExtensions.Signing));
        }

        if (OpenIddictCertificateExtensions.Resolve(store, CertificatePurposes.OidcEncryptionCode, environment) is { } encryption)
        {
            options.EncryptionCredentials.Clear();
            options.EncryptionCredentials.AddRange(encryption.Select(OpenIddictCertificateExtensions.Encryption));
        }
    }
}

internal sealed class ClientCertificateSetup(CertificateStore store, IHostEnvironment environment) : IConfigureOptions<OpenIddictClientOptions>
{
    public void Configure(OpenIddictClientOptions options)
    {
        if (OpenIddictCertificateExtensions.Resolve(store, CertificatePurposes.OidcClientCode, environment) is not { } certificates)
            return;

        options.SigningCredentials.Clear();
        options.SigningCredentials.AddRange(certificates.Select(OpenIddictCertificateExtensions.Signing));
        options.EncryptionCredentials.Clear();
        options.EncryptionCredentials.AddRange(certificates.Select(OpenIddictCertificateExtensions.Encryption));
    }
}

/// <summary>
/// OpenIddict 默认用排序后的第一张证书签发（偏向到期最晚的），待生效的新证书一注册就可能被选中——
/// 那时资源服务还没拉到它的公钥。这里改成证书库的当前证书；选项还没重建到包含它时保留 OpenIddict 的选择。
/// </summary>
internal sealed class ServerActiveCredentialSelector(CertificateStore store)
    : IOpenIddictServerHandler<OpenIddictServerEvents.GenerateTokenContext>
{
    public ValueTask HandleAsync(OpenIddictServerEvents.GenerateTokenContext context)
    {
        var signing = store.GetActive(CertificatePurposes.OidcSigningCode);
        if (context.SigningCredentials is not null &&
            context.Options.SigningCredentials.Find(credentials => OpenIddictCertificateExtensions.IsCertificate(credentials.Key, signing)) is { } selectedSigning)
        {
            context.SigningCredentials = selectedSigning;
        }

        var encryption = store.GetActive(CertificatePurposes.OidcEncryptionCode);
        if (context.EncryptionCredentials is not null &&
            context.Options.EncryptionCredentials.Find(credentials => OpenIddictCertificateExtensions.IsCertificate(credentials.Key, encryption)) is { } selectedEncryption)
        {
            context.EncryptionCredentials = selectedEncryption;
        }

        return default;
    }
}

internal sealed class ClientActiveCredentialSelector(CertificateStore store)
    : IOpenIddictClientHandler<OpenIddictClientEvents.GenerateTokenContext>
{
    public ValueTask HandleAsync(OpenIddictClientEvents.GenerateTokenContext context)
    {
        // client_assertion 用的是注册信息里的证书，不归这里管。
        if (context.TokenType == TokenTypeIdentifiers.Private.ClientAssertion)
            return default;

        var active = store.GetActive(CertificatePurposes.OidcClientCode);
        if (context.SigningCredentials is not null &&
            context.Options.SigningCredentials.Find(credentials => OpenIddictCertificateExtensions.IsCertificate(credentials.Key, active)) is { } signing)
        {
            context.SigningCredentials = signing;
        }

        if (context.EncryptionCredentials is not null &&
            context.Options.EncryptionCredentials.Find(credentials => OpenIddictCertificateExtensions.IsCertificate(credentials.Key, active)) is { } encryption)
        {
            context.EncryptionCredentials = encryption;
        }

        return default;
    }
}
