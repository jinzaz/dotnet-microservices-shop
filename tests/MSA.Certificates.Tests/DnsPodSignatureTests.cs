using MSA.CertificateService.Acme;

namespace MSA.Certificates.Tests;

[TestClass]
public sealed class DnsPodSignatureTests
{
    [TestMethod]
    public void MatchesAnIndependentlyComputedTc3Signature()
    {
        // 期望值用 openssl 按腾讯云 TC3-HMAC-SHA256 文档的步骤单独算出来，不是拿这份实现自己生成的。
        var authorization = DnsPodDnsProvider.Authorization("AKIDtest", "testkey", "{\"Limit\":3000}", 1760000000);

        Assert.AreEqual(
            "TC3-HMAC-SHA256 Credential=AKIDtest/2025-10-09/dnspod/tc3_request, SignedHeaders=content-type;host, " +
            "Signature=c1cbaf8937d59a8a755fc532c2da82ae5847a21e0a71cacaaeb63b591483eed7",
            authorization);
    }
}
