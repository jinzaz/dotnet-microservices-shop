using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MSA.Certificates;

/// <summary>
/// ASP.NET Core DataProtection 密钥环存进 CertificateDb 的 pki.data_protection_keys，按使用方分开。
/// 以前密钥环只在容器文件系统里：每次发版重建容器，BFF 会话票据（RedisTicketStore）、认证中心的登录 Cookie
/// 全部解不开，所有人掉线；多副本之间也互相解不开。表结构由 MSA.CertificateService 的迁移创建。
/// </summary>
internal sealed class CertificateDbXmlRepository(NpgsqlDataSource dataSource, string application) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var command = dataSource.CreateCommand(
            "SELECT xml FROM pki.data_protection_keys WHERE application = $1 ORDER BY id");
        command.Parameters.Add(new NpgsqlParameter { Value = application });
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read())
            elements.Add(XElement.Parse(reader.GetString(0)));
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var command = dataSource.CreateCommand(
            "INSERT INTO pki.data_protection_keys (application, friendly_name, xml, created_at) VALUES ($1, $2, $3, now())");
        command.Parameters.Add(new NpgsqlParameter { Value = application });
        command.Parameters.Add(new NpgsqlParameter { Value = friendlyName });
        command.Parameters.Add(new NpgsqlParameter { Value = element.ToString(SaveOptions.DisableFormatting) });
        command.ExecuteNonQuery();
    }
}

/// <summary>密钥环里的每把密钥用本使用方的 KEK 加密后再落库：库里的密文离开这个 KEK 毫无用处。</summary>
internal sealed class KekXmlEncryptor(CertificateKek kek) : IXmlEncryptor
{
    internal const string ElementName = "msaEncryptedKey";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var envelope = kek.Encrypt(plaintext, Context(kek.Consumer));
        var element = new XElement(ElementName,
            new XAttribute("kek", kek.Id),
            Convert.ToBase64String(envelope));
        return new EncryptedXmlInfo(element, typeof(KekXmlDecryptor));
    }

    internal static string Context(string consumer) => $"msa-dataprotection:{consumer}";
}

/// <summary>DataProtection 按类型名创建解密器，只认带 IServiceProvider 参数的公共构造函数。</summary>
internal sealed class KekXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    private readonly CertificateKek _kek = services.GetRequiredService<CertificateKek>();

    public XElement Decrypt(XElement encryptedElement)
    {
        var kekId = (string?)encryptedElement.Attribute("kek");
        if (kekId != _kek.Id)
        {
            throw new InvalidOperationException(
                $"A DataProtection key was encrypted with KEK {kekId}, but {CertificateKek.VariableName(_kek.Consumer)} is KEK {_kek.Id}.");
        }

        var plaintext = _kek.Decrypt(Convert.FromBase64String(encryptedElement.Value), KekXmlEncryptor.Context(_kek.Consumer));
        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
