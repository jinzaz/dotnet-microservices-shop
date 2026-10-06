using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using MSA.BFF.Common.BodyEncryption;
using MSA.CertificateService.Acme;
using MSA.CertificateService.Application;
using MSA.CertificateService.Domain;

namespace MSA.Certificates.Tests;

[TestClass]
public sealed class CertificateFilesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [TestMethod]
    public void PrivateKeyExportsRoundTripThroughImport()
    {
        using var generated = CertificateFiles.Generate(CertificatePurposes.OidcSigning, CertificateConsumers.Identity, Now, TimeSpan.FromDays(30));

        var pfx = CertificateFiles.ExportPrivate(generated.ChainPem, generated.PrivateKey, CertificatePurposes.OidcSigningCode, "cert", "pfx", "export-password");
        using var fromPfx = CertificateFiles.FromPfx(Convert.FromBase64String(pfx.Single().ContentBase64), "export-password");
        Assert.AreEqual(generated.Leaf.Thumbprint, fromPfx.Leaf.Thumbprint);
        CollectionAssert.AreEqual(generated.PrivateKey, fromPfx.PrivateKey);

        // 加密的 PEM 私钥：nginx / 其它工具常用格式。
        var pem = CertificateFiles.ExportPrivate(generated.ChainPem, generated.PrivateKey, CertificatePurposes.OidcSigningCode, "cert", "pem", "pem-password");
        using var fromPem = CertificateFiles.FromPem(Text(pem[0]), Text(pem[1]), "pem-password");
        Assert.AreEqual(generated.Leaf.Thumbprint, fromPem.Leaf.Thumbprint);
    }

    [TestMethod]
    public void TlsFullChainPemImportsWithTheLeafFirst()
    {
        // nginx 常见的 fullchain.pem + privkey.pem：叶子在前、中间证书在后，私钥是 EC。
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caRequest = new CertificateRequest("CN=Test CA", caKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = caRequest.CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=shop.example.com", leafKey, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("shop.example.com");
        names.AddDnsName("*.example.com");
        leafRequest.CertificateExtensions.Add(names.Build());
        using var leaf = leafRequest.Create(ca, Now.AddDays(-1), Now.AddDays(90), [1, 2, 3, 4]);

        using var material = CertificateFiles.FromPem(CertificateMaterial.ToPem([leaf, ca]), leafKey.ExportPkcs8PrivateKeyPem(), password: null);

        Assert.AreEqual(leaf.Thumbprint, material.Leaf.Thumbprint);
        Assert.AreEqual(ca.Thumbprint, material.Intermediates.Single().Thumbprint);
        CollectionAssert.AreEquivalent(new[] { "shop.example.com", "*.example.com" }, material.DnsNames);
        CertificateFiles.Validate(CertificatePurposes.Tls, CertificateConsumers.Edge, material, Now);
    }

    [TestMethod]
    public void WrongPfxPasswordIsReportedToTheOperator()
    {
        using var generated = CertificateFiles.Generate(CertificatePurposes.OidcClient, CertificateConsumers.BffWeb, Now, TimeSpan.FromDays(30));
        var pfx = CertificateFiles.ExportPrivate(generated.ChainPem, generated.PrivateKey, CertificatePurposes.OidcClientCode, "cert", "pfx", "right-password");

        Assert.ThrowsExactly<CertificateRuleException>(() => CertificateFiles.FromPfx(Convert.FromBase64String(pfx.Single().ContentBase64), "wrong-password"));
    }

    [TestMethod]
    public void PurposeRulesRejectMismatchedCertificates()
    {
        using var ecKey = CertificateFiles.Generate(CertificatePurposes.BodyEncryption, CertificateConsumers.BffWeb, Now, TimeSpan.FromDays(30));
        using var rsaKey = CertificateFiles.Generate(CertificatePurposes.OidcSigning, CertificateConsumers.Identity, Now, TimeSpan.FromDays(30));

        // 签名证书必须是 RSA；用途与使用方要对得上；没有 SAN 的证书不能当 TLS 证书。
        Assert.ThrowsExactly<CertificateRuleException>(() => CertificateFiles.Validate(CertificatePurposes.OidcSigning, CertificateConsumers.Identity, ecKey, Now));
        Assert.ThrowsExactly<CertificateRuleException>(() => CertificateFiles.Validate(CertificatePurposes.OidcSigning, CertificateConsumers.BffWeb, rsaKey, Now));
        Assert.ThrowsExactly<CertificateRuleException>(() => CertificateFiles.Validate(CertificatePurposes.Tls, CertificateConsumers.Edge, rsaKey, Now));
        CertificateFiles.Validate(CertificatePurposes.OidcSigning, CertificateConsumers.Identity, rsaKey, Now);
    }

    [TestMethod]
    public void BodyEncryptionKeysCanDoKeyAgreementAfterLoading()
    {
        using var generated = CertificateFiles.Generate(CertificatePurposes.BodyEncryption, CertificateConsumers.BffBm, Now, TimeSpan.FromDays(30));
        using var loaded = CertificateMaterial.WithPrivateKey(generated.Leaf, generated.PrivateKey, CertificatePurposes.BodyEncryptionCode);
        using var key = loaded.GetECDiffieHellmanPrivateKey();

        Assert.IsNotNull(key);
        Assert.AreEqual(CertificateKeyTypes.EcP256, CertificateMaterial.KeyTypeOf(loaded));
    }

    private static string Text(CertificateFile file) => Encoding.UTF8.GetString(Convert.FromBase64String(file.ContentBase64));
}

[TestClass]
public sealed class BodyEncryptionKeyRingTests
{
    [TestMethod]
    public void RotatedKeyStaysUsableUntilItsRetentionEnds()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var old = TestCertificates.Create(CertificatePurposes.BodyEncryption, time.Now.AddDays(-30));
        var store = TestCertificates.Store(time, CertificateConsumers.BffWeb, old);
        using var ring = new BodyEncryptionKeyRing(store, new TestWebEnvironment());
        var oldKid = ring.Current.Kid;

        var next = TestCertificates.Create(CertificatePurposes.BodyEncryption, time.Now.AddMinutes(10));
        time.Now = time.Now.AddMinutes(11);
        store.Publish([old, next]);

        Assert.AreNotEqual(oldKid, ring.Current.Kid, "The public key endpoint must hand out the new key.");
        Assert.IsTrue(ring.TryGet(oldKid, out _), "Open pages still hold the old key during its retention.");

        time.Now = time.Now.AddDays(1).AddMinutes(1);
        store.NotifyChanged();
        Assert.IsFalse(ring.TryGet(oldKid, out _));
    }

    private sealed class TestWebEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

[TestClass]
public sealed class AcmeRenewalScheduleTests
{
    [TestMethod]
    public void RenewsAfterTwoThirdsOfTheLifetimeOrWhenDomainsChange()
    {
        var notBefore = TestCertificates.Epoch;
        var certificate = View(notBefore, notBefore.AddDays(90), ["example.com", "*.example.com"]);
        var subscription = Subscription("*.example.com", "example.com");

        Assert.AreEqual(notBefore.AddDays(60), AcmeSubscriptions.RenewAt(certificate));
        Assert.IsFalse(AcmeSubscriptions.IsDue(subscription, certificate, notBefore.AddDays(59)));
        Assert.IsTrue(AcmeSubscriptions.IsDue(subscription, certificate, notBefore.AddDays(60)));
        Assert.IsTrue(AcmeSubscriptions.IsDue(Subscription("example.com"), certificate, notBefore.AddDays(1)));
        Assert.IsTrue(AcmeSubscriptions.IsDue(subscription, null, notBefore));
    }

    private static AcmeSubscription Subscription(params string[] domains) => new()
    {
        Slot = "example.com",
        Domains = domains,
        DirectoryUrl = "https://acme.test/directory",
        ContactEmail = "ops@example.com",
        CreatedBy = "test",
        Enabled = true
    };

    private static CertificateView View(DateTimeOffset notBefore, DateTimeOffset notAfter, string[] dnsNames) => new(
        Guid.NewGuid(), CertificateConsumers.Edge, CertificatePurposes.TlsCode, "example.com", "example.com", CertificateSource.Acme,
        CertificateStage.Active, null, "CN=example.com", "CN=CA", "00", "EC P-256", dnsNames, notBefore, notAfter, notBefore,
        null, null, true, false, null, notBefore, "system");
}
