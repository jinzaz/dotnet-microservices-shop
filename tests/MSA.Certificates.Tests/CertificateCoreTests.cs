using System.Security.Cryptography;

namespace MSA.Certificates.Tests;

[TestClass]
public sealed class CertificateKekTests
{
    [TestMethod]
    public void RoundTripsAndBindsTheCiphertextToItsContext()
    {
        var kek = TestCertificates.Kek(CertificateConsumers.Identity);
        var context = CertificateKek.CertificateContext(Guid.NewGuid(), kek.Consumer, CertificatePurposes.OidcSigningCode);
        var envelope = kek.Encrypt("secret"u8, context);

        CollectionAssert.AreEqual("secret"u8.ToArray(), kek.Decrypt(envelope, context));
        // 密文挪到另一行（不同的附加数据）或换一把 KEK 都解不开。
        Assert.ThrowsExactly<AuthenticationTagMismatchException>(() =>
            kek.Decrypt(envelope, CertificateKek.CertificateContext(Guid.NewGuid(), kek.Consumer, CertificatePurposes.OidcSigningCode)));
        Assert.ThrowsExactly<AuthenticationTagMismatchException>(() =>
            TestCertificates.Kek(CertificateConsumers.Identity).Decrypt(envelope, context));
    }

    [TestMethod]
    public void VariableNameFollowsTheConsumer() =>
        Assert.AreEqual("MSA_CERT_KEK_BFF_WEB", CertificateKek.VariableName(CertificateConsumers.BffWeb));
}

[TestClass]
public sealed class CertificateTimelineTests
{
    private static readonly DateTimeOffset Now = TestCertificates.Epoch;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(15);

    [TestMethod]
    public void ClassifiesEveryStageFromTimesAlone()
    {
        var retired = Entry(-40);
        var retiring = Entry(-30);
        var active = Entry(-10);
        var staged = Entry(+2);
        var expired = new CertificateTimelineEntry(Guid.NewGuid(), Now.AddDays(-400), Now.AddDays(-1), null);

        var states = CertificateTimeline.Classify([retired, retiring, active, staged, expired], Retention, Now);

        Assert.AreEqual(CertificateStage.Retired, states[retired.Id].Stage);
        // 被接替 10 天，保留期 15 天：还在退役中，并告诉调用方哪天退役。
        Assert.AreEqual(CertificateStage.Retiring, states[retiring.Id].Stage);
        Assert.AreEqual(Now.AddDays(5), states[retiring.Id].RetireAt);
        Assert.AreEqual(CertificateStage.Active, states[active.Id].Stage);
        Assert.AreEqual(CertificateStage.Staged, states[staged.Id].Stage);
        Assert.AreEqual(CertificateStage.Expired, states[expired.Id].Stage);
    }

    [TestMethod]
    public void RevokingTheActiveCertificateRollsBackToThePreviousOne()
    {
        var previous = Entry(-30);
        var revoked = Entry(-10) with { RevokedAt = Now };

        var states = CertificateTimeline.Classify([previous, revoked], Retention, Now);

        Assert.AreEqual(CertificateStage.Active, states[previous.Id].Stage);
        Assert.AreEqual(CertificateStage.Revoked, states[revoked.Id].Stage);
    }

    private static CertificateTimelineEntry Entry(int activatedDaysFromNow) =>
        new(Guid.NewGuid(), Now.AddDays(activatedDaysFromNow), Now.AddDays(365), null);
}

[TestClass]
public sealed class CertificateStoreTests
{
    [TestMethod]
    public void UsableCertificatesPutTheActiveOneFirstAndFollowTheClock()
    {
        var time = new ManualTime(TestCertificates.Epoch);
        var old = TestCertificates.Create(CertificatePurposes.BodyEncryption, time.Now.AddDays(-40));
        var current = TestCertificates.Create(CertificatePurposes.BodyEncryption, time.Now.AddDays(-1));
        var next = TestCertificates.Create(CertificatePurposes.BodyEncryption, time.Now.AddMinutes(10));
        var store = TestCertificates.Store(time, CertificateConsumers.BffWeb, old, current, next);

        // old 早已过了 1 天保留期，不再加载；next 已加载但不是当前。
        CollectionAssert.AreEqual(new[] { current.Id, next.Id }, store.GetUsable(CertificatePurposes.BodyEncryptionCode).Select(item => item.Id).ToArray());

        var state = store.DescribeState();
        time.Now = time.Now.AddMinutes(11);
        Assert.AreEqual(next.Id, store.GetActive(CertificatePurposes.BodyEncryptionCode)?.Id);
        Assert.AreNotEqual(state, store.DescribeState(), "Crossing an activation time must change the state the refresher watches.");
    }

    [TestMethod]
    public void PurposeWithoutAnActiveCertificateIsNotManagedByTheStore()
    {
        var time = new ManualTime(TestCertificates.Epoch);
        var store = TestCertificates.Store(time, CertificateConsumers.BffWeb,
            TestCertificates.Create(CertificatePurposes.OidcClient, time.Now.AddHours(1)));

        Assert.IsEmpty(store.GetUsable(CertificatePurposes.OidcClientCode));
    }

    [TestMethod]
    public void PublishingFiresTheChangeToken()
    {
        var store = TestCertificates.Store(new ManualTime(TestCertificates.Epoch), CertificateConsumers.BffWeb);
        var token = store.GetChangeToken();

        store.Publish([]);

        Assert.IsTrue(token.HasChanged);
        Assert.IsFalse(store.GetChangeToken().HasChanged);
    }
}
