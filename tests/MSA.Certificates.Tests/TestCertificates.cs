using System.Security.Cryptography;
using MSA.CertificateService.Application;

[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]

namespace MSA.Certificates.Tests;

/// <summary>手动拨动的时钟：证书阶段完全由时间推出来，测试里要能"快进"。</summary>
internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class TestCertificates
{
    public static readonly DateTimeOffset Epoch = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>生成一张某用途的证书并装上私钥，模拟证书库加载出来的样子。</summary>
    /// <param name="issuedAt">证书本身的生效时间（NotBefore），默认等于激活时间；导入的证书往往早就有效、只是排期晚一点激活。</param>
    public static StoredCertificate Create(
        CertificatePurpose purpose,
        DateTimeOffset activateAt,
        string slot = CertificatePurposes.DefaultSlot,
        DateTimeOffset? issuedAt = null)
    {
        using var material = CertificateFiles.Generate(purpose, purpose.Consumers[0], issuedAt ?? activateAt, purpose.Validity);
        var certificate = CertificateMaterial.WithPrivateKey(material.Leaf, material.PrivateKey, purpose.Code);
        return new StoredCertificate(Guid.NewGuid(), purpose.Code, slot, activateAt, new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), certificate);
    }

    public static CertificateStore Store(TimeProvider time, string consumer, params StoredCertificate[] certificates)
    {
        var store = new CertificateStore(consumer, isEnabled: true, time);
        store.Publish(certificates);
        return store;
    }

    public static CertificateKek Kek(string consumer) => new(consumer, RandomNumberGenerator.GetBytes(32));
}
