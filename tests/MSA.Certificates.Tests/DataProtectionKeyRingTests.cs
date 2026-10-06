using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MSA.Certificates.Tests;

/// <summary>
/// 密钥环落库后，换一个进程（新的 ServiceProvider）还能读回并解密：发版重启后旧 Cookie、防伪令牌仍然有效。
/// 用内存仓库代替 CertificateDb，验证的是 KEK 加密 / 解密器在真实 DataProtection 管道里的行为。
/// </summary>
[TestClass]
public sealed class DataProtectionKeyRingTests
{
    [TestMethod]
    public void PayloadProtectedBeforeARestartIsReadableAfterIt()
    {
        var repository = new MemoryXmlRepository();
        var kek = TestCertificates.Kek(CertificateConsumers.Identity);

        string protectedPayload;
        using (var before = Build(repository, kek))
            protectedPayload = before.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("hello");

        Assert.IsTrue(repository.Elements.Count > 0);
        Assert.IsFalse(repository.Elements.Any(element => element.ToString().Contains("<masterKey", StringComparison.Ordinal)), "Keys must not be stored in clear text.");

        using var after = Build(repository, kek);
        Assert.AreEqual("hello", after.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedPayload));
    }

    private static ServiceProvider Build(MemoryXmlRepository repository, CertificateKek kek)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(kek);
        services.AddDataProtection().SetApplicationName("msa-test");
        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = repository;
            options.XmlEncryptor = new KekXmlEncryptor(kek);
        });
        return services.BuildServiceProvider();
    }

    private sealed class MemoryXmlRepository : IXmlRepository
    {
        public List<XElement> Elements { get; } = [];

        public IReadOnlyCollection<XElement> GetAllElements() => Elements.Select(element => new XElement(element)).ToList();

        public void StoreElement(XElement element, string friendlyName) => Elements.Add(new XElement(element));
    }
}
