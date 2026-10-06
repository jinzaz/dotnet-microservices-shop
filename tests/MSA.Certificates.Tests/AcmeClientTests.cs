using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using MSA.CertificateService.Acme;

namespace MSA.Certificates.Tests;

/// <summary>
/// 没有真实域名时，用一个进程内的 RFC 8555 服务端把 ACME 客户端的协议细节跑一遍：JWS 签名（jwk / kid）、
/// nonce 用后即换与 badNonce 重试、普通 + 通配符域名的授权、key authorization、CSR、下载证书链。
/// 服务端对每个请求都验签、校验 url 和 nonce，任何一处不符合规范都会失败。
/// </summary>
[TestClass]
public sealed class AcmeClientTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task IssuesAWildcardCertificateAgainstAStrictAcmeServer()
    {
        await using var server = await FakeAcmeServer.StartAsync();
        using var accountKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var client = new AcmeClient(server.Http, new Uri("http://localhost/directory"), accountKey);
        var cancellationToken = TestContext.CancellationToken;
        string[] domains = ["example.com", "*.example.com"];

        await client.RegisterAccountAsync("ops@example.com", cancellationToken);
        var (orderUrl, order) = await client.CreateOrderAsync(domains, cancellationToken);
        Assert.AreEqual(1, server.BadNoncesSent, "The first order request is answered with badNonce and must be retried.");

        foreach (var authorizationUrl in order.Authorizations)
        {
            var authorization = await client.GetAuthorizationAsync(authorizationUrl, cancellationToken);
            var challenge = authorization.Challenges.Single(item => item.Type == "dns-01");
            Assert.AreEqual(server.ExpectedKeyAuthorization(challenge.Token), client.KeyAuthorization(challenge.Token));
            await client.AnswerChallengeAsync(challenge.Url, cancellationToken);
            await client.WaitForAuthorizationAsync(authorizationUrl, TimeSpan.FromSeconds(30), cancellationToken);
        }

        using var certificateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateUrl = await client.FinalizeAsync(orderUrl, AcmeIssuer.CreateSigningRequest(certificateKey, domains), TimeSpan.FromSeconds(30), cancellationToken);
        var chain = CertificateMaterial.ReadChain(await client.DownloadCertificateAsync(certificateUrl, cancellationToken));

        Assert.HasCount(2, chain, "Leaf and issuer.");
        var names = chain[0].Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames().ToArray();
        CollectionAssert.AreEquivalent(domains, names);
        using var leafKey = chain[0].GetECDsaPublicKey();
        CollectionAssert.AreEqual(certificateKey.ExportSubjectPublicKeyInfo(), leafKey!.ExportSubjectPublicKeyInfo());
    }

    [TestMethod]
    public void DnsTxtValueIsTheHashOfTheKeyAuthorization()
    {
        var expected = Base64Url.EncodeToString(SHA256.HashData("token.thumbprint"u8));
        Assert.AreEqual(expected, AcmeClient.DnsTxtValue("token.thumbprint"));
    }

    private sealed class FakeAcmeServer : IAsyncDisposable
    {
        private const string Base = "http://localhost";
        private readonly WebApplication _app;
        private readonly ConcurrentDictionary<string, bool> _nonces = new();
        private readonly ConcurrentDictionary<string, JsonElement> _accounts = new();
        private readonly List<Authorization> _authorizations = [];
        private readonly ECDsa _caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2 _ca;
        private string? _certificatePem;

        private FakeAcmeServer(WebApplication app)
        {
            _app = app;
            var request = new CertificateRequest("CN=Fake ACME CA", _caKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            _ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        public HttpClient Http { get; private set; } = null!;
        public int BadNoncesSent { get; private set; }

        public static async Task<FakeAcmeServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            var server = new FakeAcmeServer(builder.Build());
            server.Map();
            await server._app.StartAsync();
            server.Http = server._app.GetTestClient();
            return server;
        }

        public string ExpectedKeyAuthorization(string token)
        {
            var jwk = _accounts.Values.Single();
            // RFC 7638：只取必需字段、按字段名排序。
            var canonical = $$"""{"crv":"{{jwk.GetProperty("crv").GetString()}}","kty":"{{jwk.GetProperty("kty").GetString()}}","x":"{{jwk.GetProperty("x").GetString()}}","y":"{{jwk.GetProperty("y").GetString()}}"}""";
            return $"{token}.{Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            _ca.Dispose();
            _caKey.Dispose();
        }

        private void Map()
        {
            _app.Use(async (context, next) =>
            {
                var nonce = Guid.NewGuid().ToString("N");
                _nonces[nonce] = true;
                context.Response.Headers["Replay-Nonce"] = nonce;
                await next(context);
            });

            _app.MapGet("/directory", () => Results.Json(new
            {
                newNonce = $"{Base}/new-nonce",
                newAccount = $"{Base}/new-account",
                newOrder = $"{Base}/new-order"
            }));
            _app.MapMethods("/new-nonce", ["HEAD"], () => Results.Ok());

            _app.MapPost("/new-account", async (HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                if (request.Problem is not null)
                    return request.Problem;
                Assert.IsTrue(request.Payload!.Value.GetProperty("termsOfServiceAgreed").GetBoolean());
                _accounts[$"{Base}/account/1"] = request.Header.GetProperty("jwk");
                context.Response.Headers.Location = $"{Base}/account/1";
                return Results.Json(new { status = "valid" }, statusCode: StatusCodes.Status201Created);
            });

            _app.MapPost("/new-order", async (HttpContext context) =>
            {
                var request = await VerifyAsync(context, failWithBadNonceOnce: true);
                if (request.Problem is not null)
                    return request.Problem;
                foreach (var identifier in request.Payload!.Value.GetProperty("identifiers").EnumerateArray())
                {
                    var value = identifier.GetProperty("value").GetString()!;
                    _authorizations.Add(new Authorization(value.TrimStart('*', '.'), value.StartsWith("*.", StringComparison.Ordinal), Guid.NewGuid().ToString("N")));
                }
                context.Response.Headers.Location = $"{Base}/order/1";
                return Results.Json(Order(), statusCode: StatusCodes.Status201Created);
            });

            _app.MapPost("/authz/{index:int}", async (int index, HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                if (request.Problem is not null)
                    return request.Problem;
                Assert.IsNull(request.Payload, "Authorizations are fetched with POST-as-GET.");
                var authorization = _authorizations[index];
                return Results.Json(new
                {
                    identifier = new { type = "dns", value = authorization.Domain },
                    status = authorization.Answered ? "valid" : "pending",
                    wildcard = authorization.Wildcard,
                    challenges = new[] { new { type = "dns-01", url = $"{Base}/challenge/{index}", token = authorization.Token, status = "pending" } }
                });
            });

            _app.MapPost("/challenge/{index:int}", async (int index, HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                if (request.Problem is not null)
                    return request.Problem;
                Assert.AreEqual(JsonValueKind.Object, request.Payload!.Value.ValueKind);
                Assert.IsFalse(request.Payload.Value.EnumerateObject().Any(), "Challenges are answered with an empty object.");
                _authorizations[index].Answered = true;
                return Results.Json(new { type = "dns-01", url = $"{Base}/challenge/{index}", token = _authorizations[index].Token, status = "processing" });
            });

            _app.MapPost("/order/1", async (HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                return request.Problem ?? Results.Json(Order());
            });

            _app.MapPost("/order/1/finalize", async (HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                if (request.Problem is not null)
                    return request.Problem;
                if (_authorizations.Any(authorization => !authorization.Answered))
                    return Problem("orderNotReady", "Authorizations are still pending.", StatusCodes.Status403Forbidden);

                var csr = CertificateRequest.LoadSigningRequest(
                    Base64Url.DecodeFromChars(request.Payload!.Value.GetProperty("csr").GetString()),
                    HashAlgorithmName.SHA256,
                    CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
                using var leaf = csr.Create(_ca, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90), RandomNumberGenerator.GetBytes(16));
                _certificatePem = CertificateMaterial.ToPem([leaf, _ca]);
                return Results.Json(Order());
            });

            _app.MapPost("/certificate/1", async (HttpContext context) =>
            {
                var request = await VerifyAsync(context);
                return request.Problem ?? Results.Text(_certificatePem!, "application/pem-certificate-chain");
            });
        }

        private object Order() => new
        {
            status = _certificatePem is not null ? "valid" : _authorizations.All(authorization => authorization.Answered) ? "ready" : "pending",
            authorizations = _authorizations.Select((_, index) => $"{Base}/authz/{index}").ToArray(),
            finalize = $"{Base}/order/1/finalize",
            certificate = _certificatePem is null ? null : $"{Base}/certificate/1"
        };

        /// <summary>按 RFC 8555 §6 校验：jose+json、ES256、nonce 有效且未用过、url 与请求一致、签名正确。</summary>
        private async Task<SignedRequest> VerifyAsync(HttpContext context, bool failWithBadNonceOnce = false)
        {
            Assert.AreEqual("application/jose+json", context.Request.ContentType);
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
            var protectedPart = body.GetProperty("protected").GetString()!;
            var payloadPart = body.GetProperty("payload").GetString()!;
            var header = JsonDocument.Parse(Base64Url.DecodeFromChars(protectedPart)).RootElement;

            Assert.AreEqual("ES256", header.GetProperty("alg").GetString());
            Assert.AreEqual($"{Base}{context.Request.Path}", header.GetProperty("url").GetString());
            if (!_nonces.TryRemove(header.GetProperty("nonce").GetString()!, out _))
                return new SignedRequest(header, null, Problem("badNonce", "Unknown or reused nonce."));
            if (failWithBadNonceOnce && BadNoncesSent == 0)
            {
                BadNoncesSent++;
                return new SignedRequest(header, null, Problem("badNonce", "Injected by the test."));
            }

            var jwk = header.TryGetProperty("jwk", out var embedded) ? embedded : _accounts[header.GetProperty("kid").GetString()!];
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()),
                    Y = Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString())
                }
            });
            var signature = Base64Url.DecodeFromChars(body.GetProperty("signature").GetString());
            Assert.IsTrue(key.VerifyData(Encoding.ASCII.GetBytes($"{protectedPart}.{payloadPart}"), signature, HashAlgorithmName.SHA256), "JWS signature");

            JsonElement? payload = payloadPart.Length == 0 ? null : JsonDocument.Parse(Base64Url.DecodeFromChars(payloadPart)).RootElement;
            return new SignedRequest(header, payload, null);
        }

        private static IResult Problem(string type, string detail, int status = StatusCodes.Status400BadRequest) =>
            Results.Json(new { type = $"urn:ietf:params:acme:error:{type}", detail }, statusCode: status, contentType: "application/problem+json");

        private sealed record SignedRequest(JsonElement Header, JsonElement? Payload, IResult? Problem);

        private sealed record Authorization(string Domain, bool Wildcard, string Token)
        {
            public bool Answered { get; set; }
        }
    }
}
