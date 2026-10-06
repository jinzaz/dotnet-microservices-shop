using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Server;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace MSA.Certificates.Tests;

/// <summary>
/// 在真实的 OpenIddict 管道上验证"换证书不重启"：JWKS 提前发布待生效密钥、签发只用当前证书、
/// 时间到点后切到新证书而旧证书仍在 JWKS 里，认证中心自己的验证端（UseLocalServer）跟着重建。
/// </summary>
[TestClass]
public sealed class OpenIddictRotationTests
{
    [TestMethod]
    public async Task ServerSignsWithTheActiveCertificateAndPrePublishesTheNextOne()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var current = TestCertificates.Create(CertificatePurposes.OidcSigning, time.Now.AddDays(-1));
        // 像导入的证书一样早就有效（NotBefore 已过、到期更晚），只是排期 2 天后才激活：OpenIddict 自己会优先选它。
        var next = TestCertificates.Create(CertificatePurposes.OidcSigning, time.Now.AddDays(2), issuedAt: time.Now.AddHours(-1));
        var encryption = TestCertificates.Create(CertificatePurposes.OidcEncryption, time.Now.AddDays(-1));
        var store = TestCertificates.Store(time, CertificateConsumers.Identity, current, next, encryption);
        await using var app = await StartServerAsync(store);
        using var http = app.GetTestClient();

        CollectionAssert.AreEquivalent(new[] { KeyId(current), KeyId(next) }, await JwksKeyIdsAsync(http));
        // 必须是当前证书签名：资源服务此时可能还没拉到 next 的公钥。
        Assert.AreEqual(KeyId(current), await IssueTokenKeyIdAsync(http));

        time.Now = time.Now.AddDays(2).AddMinutes(1);
        store.NotifyChanged();

        Assert.AreEqual(KeyId(next), await IssueTokenKeyIdAsync(http));
        CollectionAssert.AreEquivalent(new[] { KeyId(current), KeyId(next) }, await JwksKeyIdsAsync(http), "The replaced key stays published during its retention.");
    }

    [TestMethod]
    public async Task MissingCertificatesFailAtStartupInsteadOfOnTheFirstLogin()
    {
        // 生产环境：证书库里没有当前证书、也没配环境变量 pfx。
        var empty = TestCertificates.Store(new ManualTime(DateTimeOffset.UtcNow), CertificateConsumers.Identity);

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartServerAsync(empty));
    }

    [TestMethod]
    public async Task LocalValidationIsRebuiltWithTheServerKeys()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var signing = TestCertificates.Create(CertificatePurposes.OidcSigning, time.Now.AddDays(-1));
        var encryption = TestCertificates.Create(CertificatePurposes.OidcEncryption, time.Now.AddDays(-1));
        var store = TestCertificates.Store(time, CertificateConsumers.Identity, signing, encryption);
        await using var app = await StartServerAsync(store);
        var validation = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictValidationOptions>>();
        Assert.HasCount(1, validation.CurrentValue.Configuration!.SigningKeys);

        var added = TestCertificates.Create(CertificatePurposes.OidcSigning, time.Now.AddDays(2));
        store.Publish([signing, encryption, added]);

        Assert.HasCount(2, validation.CurrentValue.Configuration!.SigningKeys);
    }

    [TestMethod]
    public void ClientOptionsRebuildCleanlyWhenCertificatesChange()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var first = TestCertificates.Create(CertificatePurposes.OidcClient, time.Now.AddDays(-1));
        var store = TestCertificates.Store(time, CertificateConsumers.BffWeb, first);
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddLogging();
        services.AddOpenIddict().AddClient(options =>
        {
            options.AllowClientCredentialsFlow();
            options.UseSystemNetHttp();
            options.AddRegistration(new OpenIddictClientRegistration
            {
                Issuer = new Uri("https://identity.test/"),
                ClientId = "client",
                ClientSecret = "secret"
            });
            options.UseMsaCertificates();
        });
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>();
        Assert.AreEqual(first.Certificate.Thumbprint, ((X509SecurityKey)monitor.CurrentValue.SigningCredentials.Single().Key).Certificate.Thumbprint);

        var second = TestCertificates.Create(CertificatePurposes.OidcClient, time.Now.AddMinutes(10));
        store.Publish([first, second]);

        // 重建后注册信息只有一份、两张证书都在（旧的继续解密登录中的 state token）。
        Assert.HasCount(1, monitor.CurrentValue.Registrations);
        Assert.HasCount(2, monitor.CurrentValue.SigningCredentials);
        Assert.HasCount(2, monitor.CurrentValue.EncryptionCredentials);
    }

    private static async Task<WebApplication> StartServerAsync(CertificateStore store)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddOpenIddict()
            .AddServer(options =>
            {
                options.EnableDegradedMode();
                options.SetIssuer(new Uri("https://identity.test/"));
                options.SetTokenEndpointUris("connect/token");
                options.SetJsonWebKeySetEndpointUris(".well-known/jwks");
                options.AllowClientCredentialsFlow();
                options.DisableAccessTokenEncryption();
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.UseMsaCertificates();
                // 降级模式没有客户端存储：测试里直接放行并签发一个只有 sub 的令牌。
                options.AddEventHandler<ValidateTokenRequestContext>(handler => handler.UseInlineHandler(_ => default));
                options.AddEventHandler<HandleTokenRequestContext>(handler => handler.UseInlineHandler(context =>
                {
                    var identity = new ClaimsIdentity("test");
                    identity.AddClaim(new Claim(Claims.Subject, "service").SetDestinations(Destinations.AccessToken));
                    context.SignIn(new ClaimsPrincipal(identity));
                    return default;
                }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        var app = builder.Build();
        await app.StartAsync();
        return app;
    }

    private static async Task<string?> IssueTokenKeyIdAsync(HttpClient http)
    {
        using var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = GrantTypes.ClientCredentials,
            ["client_id"] = "service",
            ["client_secret"] = "secret"
        }));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(response.IsSuccessStatusCode, body.ToString());
        return new JsonWebToken(body.GetProperty("access_token").GetString()).Kid;
    }

    private static async Task<string[]> JwksKeyIdsAsync(HttpClient http)
    {
        var jwks = await http.GetFromJsonAsync<JsonElement>("/.well-known/jwks");
        return jwks.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!).ToArray();
    }

    private static string KeyId(StoredCertificate certificate) => new X509SecurityKey(certificate.Certificate).KeyId;

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
