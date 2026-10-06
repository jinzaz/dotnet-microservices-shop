using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSA.BFF.Common.BodyEncryption;

namespace MSA.BFF.Common.Tests;

/// <summary>
/// 浏览器 ⇄ BFF 的 body 加密中间件：真实跑一遍 ASP.NET Core 管道（TestServer），
/// 不只是验证加密算法本身——ECDH/HKDF/AES-GCM 的跨平台互操作已经在设计阶段用真实浏览器 WebCrypto
/// 核对过字节级一致，这里要盖住的是中间件接入管道之后的行为：换 Request/Response.Body、
/// 没带密钥头的请求是否透传、[SkipBodyEncryption] 是否生效、坏密文/坏头是否体面地 400 而不是 500。
/// </summary>
[TestClass]
public sealed class BodyEncryptionMiddlewareTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private BodyEncryptionServerKey _serverKey = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = Environments.Development; // 没配证书时自动生成一把临时密钥
        builder.Services.AddRouting();
        builder.Services.AddBffBodyEncryption();

        _app = builder.Build();
        _app.UseBffBodyEncryption();
        _app.MapBodyEncryptionPublicKeyEndpoint();
        _app.MapPost("/echo", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"echo\":{body}}}");
        });
        _app.MapGet("/whoami", () => Results.Json(new { name = "test-user" }));
        _app.MapGet("/skip-me", () => Results.Json(new { secret = "should never be encrypted" }))
            .WithMetadata(new SkipBodyEncryptionAttribute());

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _serverKey = _app.Services.GetRequiredService<BodyEncryptionKeyRing>().Current;
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _app.StopAsync();

    [TestMethod]
    public async Task EncryptedRequestRoundTripsThroughTheRealPipeline()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (c2s, s2c) = DeriveKeys(client, _serverKey.PublicKeyRaw);

        using var request = EncryptedRequest(HttpMethod.Post, "/echo", client, c2s,
            "{\"hello\":\"world\"}"u8.ToArray());
        var response = await _client.SendAsync(request);

        Assert.AreEqual(BodyEncryptionMiddleware.ContentType, response.Content.Headers.ContentType?.MediaType);
        var decrypted = await DecryptResponseAsync(response, s2c);
        Assert.AreEqual("{\"echo\":{\"hello\":\"world\"}}", decrypted);
    }

    [TestMethod]
    public async Task GetRequestWithNoBodyStillGetsAnEncryptedResponse()
    {
        // GET 没有请求体可解密，但密钥在头里不在 body 里，响应照样能加密——这是把密钥放头里而不是
        // 只放请求体里的原因，否则纯读接口（用户信息、订单详情...）没法受益于这套机制。
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (_, s2c) = DeriveKeys(client, _serverKey.PublicKeyRaw);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add(BodyEncryptionMiddleware.EphemeralKeyHeader, Convert.ToBase64String(ExportRawPublicKey(client)));
        var response = await _client.SendAsync(request);

        Assert.AreEqual(BodyEncryptionMiddleware.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("{\"name\":\"test-user\"}", await DecryptResponseAsync(response, s2c));
    }

    [TestMethod]
    public async Task RequestWithoutTheKeyHeaderIsNotTouched()
    {
        var response = await _client.GetAsync("/whoami");
        Assert.AreNotEqual(BodyEncryptionMiddleware.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("{\"name\":\"test-user\"}", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task SkipAttributeWinsEvenWhenTheClientSendsAKey()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/skip-me");
        request.Headers.Add(BodyEncryptionMiddleware.EphemeralKeyHeader, Convert.ToBase64String(ExportRawPublicKey(client)));
        var response = await _client.SendAsync(request);

        Assert.AreNotEqual(BodyEncryptionMiddleware.ContentType, response.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "should never be encrypted");
    }

    [TestMethod]
    public async Task TamperedCiphertextFailsCleanlyInsteadOfCrashing()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (c2s, _) = DeriveKeys(client, _serverKey.PublicKeyRaw);

        using var request = EncryptedRequest(HttpMethod.Post, "/echo", client, c2s, "{\"a\":1}"u8.ToArray(), corruptTag: true);
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MalformedKeyHeaderFailsCleanlyInsteadOfCrashing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add(BodyEncryptionMiddleware.EphemeralKeyHeader, "not-a-valid-key!!");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PublicKeyEndpointAnnouncesTheKeyIdAndIsNeverCached()
    {
        var response = await _client.GetAsync("/bff/security/public-key");

        Assert.AreEqual(_serverKey.Kid, response.Headers.GetValues(BodyEncryptionMiddleware.KeyIdHeader).Single());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        CollectionAssert.AreEqual(_serverKey.PublicKeyRaw, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task RequestCarryingTheCurrentKeyIdRoundTrips()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (c2s, s2c) = DeriveKeys(client, _serverKey.PublicKeyRaw);

        using var request = EncryptedRequest(HttpMethod.Post, "/echo", client, c2s, "{\"n\":1}"u8.ToArray());
        request.Headers.Add(BodyEncryptionMiddleware.KeyIdHeader, _serverKey.Kid);
        var response = await _client.SendAsync(request);

        Assert.AreEqual("{\"echo\":{\"n\":1}}", await DecryptResponseAsync(response, s2c));
    }

    [TestMethod]
    public async Task RetiredKeyIdIsRejectedWithASignalToRefetchTheKey()
    {
        // 浏览器缓存的公钥已经退役：明确告诉它重拉，而不是用当前密钥加密一个它解不开的响应。
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (c2s, _) = DeriveKeys(client, _serverKey.PublicKeyRaw);

        using var request = EncryptedRequest(HttpMethod.Post, "/echo", client, c2s, "{\"n\":1}"u8.ToArray());
        request.Headers.Add(BodyEncryptionMiddleware.KeyIdHeader, "retired-key-id");
        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains(BodyEncryptionMiddleware.UnknownKeyHeader));
        Assert.AreNotEqual(BodyEncryptionMiddleware.ContentType, response.Content.Headers.ContentType?.MediaType);
    }

    private static byte[] ExportRawPublicKey(ECDiffieHellman key)
    {
        var p = key.ExportParameters(false);
        var raw = new byte[65];
        raw[0] = 0x04;
        p.Q.X!.CopyTo(raw, 1);
        p.Q.Y!.CopyTo(raw, 33);
        return raw;
    }

    private static ECDiffieHellman ImportRawPublicKey(byte[] raw) =>
        ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = raw[1..33], Y = raw[33..65] }
        });

    private static (byte[] C2S, byte[] S2C) DeriveKeys(ECDiffieHellman clientKey, byte[] serverPublicRaw)
    {
        using var serverPublicKey = ImportRawPublicKey(serverPublicRaw);
        var secret = clientKey.DeriveRawSecretAgreement(serverPublicKey.PublicKey);
        return (
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: "msa-body-c2s"u8.ToArray()),
            HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: "msa-body-s2c"u8.ToArray()));
    }

    private static HttpRequestMessage EncryptedRequest(
        HttpMethod method, string path, ECDiffieHellman clientKey, byte[] c2sKey, byte[] plaintext, bool corruptTag = false)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(c2sKey, 16))
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        if (corruptTag) tag[0] ^= 0xFF;

        var envelope = new byte[12 + ciphertext.Length + 16];
        nonce.CopyTo(envelope, 0);
        ciphertext.CopyTo(envelope, 12);
        tag.CopyTo(envelope, 12 + ciphertext.Length);

        var request = new HttpRequestMessage(method, path)
        {
            Content = new ByteArrayContent(envelope)
        };
        request.Headers.Add(BodyEncryptionMiddleware.EphemeralKeyHeader, Convert.ToBase64String(ExportRawPublicKey(clientKey)));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(BodyEncryptionMiddleware.ContentType);
        return request;
    }

    private static async Task<string> DecryptResponseAsync(HttpResponseMessage response, byte[] s2cKey)
    {
        var envelope = await response.Content.ReadAsByteArrayAsync();
        var nonce = envelope.AsSpan(0, 12);
        var cipherAndTag = envelope.AsSpan(12);
        var tag = cipherAndTag[^16..];
        var ciphertext = cipherAndTag[..^16];
        var plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(s2cKey, 16);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
