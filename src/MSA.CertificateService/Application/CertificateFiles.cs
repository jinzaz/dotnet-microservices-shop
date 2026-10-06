using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MSA.Certificates;

namespace MSA.CertificateService.Application;

/// <summary>入库前的证书材料：叶子证书（公钥部分）、中间证书（按签发顺序）、PKCS#8 私钥。</summary>
public sealed class CertificateMaterialSet(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> intermediates, byte[] privateKey) : IDisposable
{
    public X509Certificate2 Leaf { get; } = leaf;
    public IReadOnlyList<X509Certificate2> Intermediates { get; } = intermediates;
    public byte[] PrivateKey { get; } = privateKey;

    public string ChainPem => CertificateMaterial.ToPem([Leaf, .. Intermediates]);

    public string[] DnsNames =>
        Leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault()?.EnumerateDnsNames().ToArray() ?? [];

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(PrivateKey);
        Leaf.Dispose();
        foreach (var certificate in Intermediates)
            certificate.Dispose();
    }
}

public sealed record CertificateFile(string FileName, string ContentType, string ContentBase64);

/// <summary>证书文件的解析（导入）、生成与导出。只处理格式，不碰数据库。</summary>
public static class CertificateFiles
{
    private const int RsaKeySize = 3072;
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private static readonly Pkcs12ExportPbeParameters PfxEncryption = Pkcs12ExportPbeParameters.Pbes2Aes256Sha256;
    private static readonly PbeParameters PemEncryption = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    /// <exception cref="CertificateRuleException">文件解不开、口令不对、找不到带私钥的证书。</exception>
    public static CertificateMaterialSet FromPfx(byte[] pfx, string? password)
    {
        X509Certificate2Collection collection;
        try
        {
            collection = X509CertificateLoader.LoadPkcs12Collection(
                pfx, password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException exception)
        {
            throw new CertificateRuleException("PFX 文件无法解析，或口令不正确。", exception);
        }

        var withKey = collection.Where(certificate => certificate.HasPrivateKey).ToList();
        if (withKey.Count != 1)
        {
            DisposeAll(collection);
            throw new CertificateRuleException("PFX 里必须恰好有一张带私钥的证书。");
        }

        var leaf = withKey[0];
        var others = collection.Where(certificate => certificate != leaf).ToList();
        try
        {
            var privateKey = ExportPrivateKey(leaf);
            return new CertificateMaterialSet(PublicPart(leaf), OrderIssuers(leaf, others), privateKey);
        }
        catch
        {
            others.ForEach(certificate => certificate.Dispose());
            throw;
        }
        finally
        {
            leaf.Dispose();
        }
    }

    /// <param name="certificatePem">叶子证书，可以跟着中间证书（fullchain.pem）。</param>
    /// <param name="privateKeyPem">PKCS#8 / PKCS#1 / SEC1 私钥；加密的 PKCS#8 需要口令。</param>
    public static CertificateMaterialSet FromPem(string certificatePem, string privateKeyPem, string? password)
    {
        X509Certificate2 leaf;
        X509Certificate2Collection chain;
        try
        {
            leaf = string.IsNullOrEmpty(password)
                ? X509Certificate2.CreateFromPem(certificatePem, privateKeyPem)
                : X509Certificate2.CreateFromEncryptedPem(certificatePem, privateKeyPem, password);
            chain = CertificateMaterial.ReadChain(certificatePem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new CertificateRuleException("PEM 无法解析、私钥与证书不匹配，或私钥口令不正确。", exception);
        }

        var others = chain.Where(certificate => certificate.Thumbprint != leaf.Thumbprint).ToList();
        foreach (var duplicate in chain.Where(certificate => certificate.Thumbprint == leaf.Thumbprint))
            duplicate.Dispose();
        try
        {
            return new CertificateMaterialSet(PublicPart(leaf), OrderIssuers(leaf, others), ExportPrivateKey(leaf));
        }
        catch
        {
            others.ForEach(certificate => certificate.Dispose());
            throw;
        }
        finally
        {
            leaf.Dispose();
        }
    }

    /// <summary>
    /// 为内部用途生成自签名证书。生效时间晚于现在时 NotBefore 就设成生效时间：即使哪里退回到 OpenIddict 自己挑证书，
    /// 也不会提前用它签名。
    /// </summary>
    public static CertificateMaterialSet Generate(CertificatePurpose purpose, string consumer, DateTimeOffset activateAt, TimeSpan validity)
    {
        if (!purpose.CanGenerate)
            throw new CertificateRuleException($"{purpose.DisplayName}不能由平台生成，请导入或使用 ACME。");

        var subject = new X500DistinguishedName($"CN=MSA {consumer} {purpose.Code}, O=MSA");
        var notBefore = activateAt.AddMinutes(-5);
        var notAfter = activateAt + validity;
        if (purpose.GeneratedKey == CertificateKeyTypes.EcP256)
        {
            // 用途只有 keyAgreement（跟 scripts/new-bodyenc-certs 一致）：CreateSelfSigned 会把私钥当 ECDSA 装回证书，
            // 而这种用途的证书不允许，所以用签名生成器只产出公钥证书，私钥另以 PKCS#8 保存。
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
            AddExtensions(request, X509KeyUsageFlags.KeyAgreement);
            var certificate = request.Create(subject, X509SignatureGenerator.CreateForECDsa(ecdsa), notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
            return new CertificateMaterialSet(certificate, [], ecdsa.ExportPkcs8PrivateKey());
        }

        using var rsa = RSA.Create(RsaKeySize);
        var rsaRequest = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddExtensions(rsaRequest, RequiredUsage(purpose.Code));
        using var rsaCertificate = rsaRequest.CreateSelfSigned(notBefore, notAfter);
        return new CertificateMaterialSet(PublicPart(rsaCertificate), [], rsa.ExportPkcs8PrivateKey());
    }

    /// <summary>
    /// 校验证书是否适合这个用途，不合适时抛 <see cref="CertificateRuleException"/>，信息直接给后台用户看。
    /// </summary>
    public static void Validate(CertificatePurpose purpose, string consumer, CertificateMaterialSet material, DateTimeOffset activateAt)
    {
        var leaf = material.Leaf;
        if (!purpose.AllowsConsumer(consumer))
            throw new CertificateRuleException($"{purpose.DisplayName}只能给 {string.Join(" / ", purpose.Consumers)} 使用。");

        var keyType = CertificateMaterial.KeyTypeOf(leaf);
        if (keyType == CertificateKeyTypes.None || !purpose.AcceptedKeys.HasFlag(keyType))
            throw new CertificateRuleException($"{purpose.DisplayName}不接受 {CertificateMaterial.DescribeKey(leaf)} 密钥，允许：{purpose.AcceptedKeys}。");

        if (leaf.NotAfter.ToUniversalTime() <= activateAt.UtcDateTime)
            throw new CertificateRuleException($"证书在生效时间之前就已过期（{leaf.NotAfter:yyyy-MM-dd HH:mm}）。");
        if (leaf.NotBefore.ToUniversalTime() > activateAt.UtcDateTime.AddMinutes(5))
            throw new CertificateRuleException($"证书 {leaf.NotBefore:yyyy-MM-dd HH:mm} 才开始有效，晚于生效时间。");

        var usage = leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault()?.KeyUsages;
        var required = RequiredUsage(purpose.Code);
        if (usage is { } declared && purpose.Code != CertificatePurposes.TlsCode && (declared & required) != required)
            throw new CertificateRuleException($"证书的密钥用途（{declared}）缺少 {required}。");

        if (purpose.Code == CertificatePurposes.TlsCode)
        {
            if (material.DnsNames.Length == 0)
                throw new CertificateRuleException("TLS 证书没有 DNS 名称（SAN）。");

            var serverAuth = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault()?.EnhancedKeyUsages;
            if (serverAuth is not null && serverAuth.Cast<Oid>().All(oid => oid.Value != ServerAuthOid))
                throw new CertificateRuleException("TLS 证书的扩展用途里没有服务器身份验证（serverAuth）。");
        }
    }

    public static CertificateFile[] ExportPublic(string chainPem, string baseName, string format)
    {
        var chain = CertificateMaterial.ReadChain(chainPem);
        try
        {
            return format switch
            {
                "pem" => [Text($"{baseName}.fullchain.pem", chainPem)],
                "der" => [new CertificateFile($"{baseName}.cer", "application/pkix-cert", Convert.ToBase64String(chain[0].Export(X509ContentType.Cert)))],
                _ => throw new CertificateRuleException("公钥导出格式只能是 pem 或 der。")
            };
        }
        finally
        {
            DisposeAll(chain);
        }
    }

    /// <param name="password">PFX 必填；PEM 填了就导出加密的 PKCS#8，不填导出明文私钥（nginx 直接能用）。</param>
    public static CertificateFile[] ExportPrivate(string chainPem, byte[] privateKey, string purpose, string baseName, string format, string? password)
    {
        if (format == "pem")
        {
            using var key = ImportKey(chainPem, privateKey, purpose);
            var keyPem = string.IsNullOrEmpty(password)
                ? key.ExportPkcs8PrivateKeyPem()
                : key.ExportEncryptedPkcs8PrivateKeyPem(password, PemEncryption);
            return [Text($"{baseName}.fullchain.pem", chainPem), Text($"{baseName}.key.pem", keyPem)];
        }

        if (format != "pfx")
            throw new CertificateRuleException("私钥导出格式只能是 pfx 或 pem。");
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new CertificateRuleException("导出 PFX 必须设置至少 8 位的口令。");

        var chain = CertificateMaterial.ReadChain(chainPem);
        try
        {
            using var leaf = CertificateMaterial.WithPrivateKey(chain[0], privateKey, purpose);
            var collection = new X509Certificate2Collection(leaf);
            collection.AddRange(chain.Skip(1).ToArray());
            var pfx = collection.ExportPkcs12(PfxEncryption, password);
            return [new CertificateFile($"{baseName}.pfx", "application/x-pkcs12", Convert.ToBase64String(pfx))];
        }
        finally
        {
            DisposeAll(chain);
        }
    }

    /// <summary>每种用途的证书至少要声明的密钥用途（证书带 KeyUsage 扩展时才检查）。</summary>
    private static X509KeyUsageFlags RequiredUsage(string purpose) => purpose switch
    {
        CertificatePurposes.OidcSigningCode => X509KeyUsageFlags.DigitalSignature,
        CertificatePurposes.OidcEncryptionCode => X509KeyUsageFlags.KeyEncipherment,
        CertificatePurposes.OidcClientCode => X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
        CertificatePurposes.BodyEncryptionCode => X509KeyUsageFlags.KeyAgreement,
        _ => X509KeyUsageFlags.DigitalSignature
    };

    private static void AddExtensions(CertificateRequest request, X509KeyUsageFlags usage)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
    }

    private static AsymmetricAlgorithm ImportKey(string chainPem, byte[] privateKey, string purpose)
    {
        var chain = CertificateMaterial.ReadChain(chainPem);
        try
        {
            using var leaf = CertificateMaterial.WithPrivateKey(chain[0], privateKey, purpose);
            return (AsymmetricAlgorithm?)leaf.GetRSAPrivateKey()
                ?? (AsymmetricAlgorithm?)leaf.GetECDsaPrivateKey()
                ?? leaf.GetECDiffieHellmanPrivateKey()
                ?? throw new CryptographicException("The certificate has no private key.");
        }
        finally
        {
            DisposeAll(chain);
        }
    }

    private static byte[] ExportPrivateKey(X509Certificate2 certificate)
    {
        try
        {
            return CertificateMaterial.ExportPkcs8(certificate);
        }
        catch (CryptographicException exception)
        {
            throw new CertificateRuleException("证书的私钥不可导出，无法存入证书库。", exception);
        }
    }

    /// <summary>只留公钥部分：私钥另以 PKCS#8 保存，避免带着平台相关的密钥句柄到处传。</summary>
    private static X509Certificate2 PublicPart(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadCertificate(certificate.RawData);

    /// <summary>从叶子开始沿"签发者 = 主题"往上排中间证书；接不上的按原顺序放在后面。</summary>
    private static List<X509Certificate2> OrderIssuers(X509Certificate2 leaf, List<X509Certificate2> remaining)
    {
        var ordered = new List<X509Certificate2>();
        var current = leaf;
        while (remaining.Find(candidate => candidate.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)) is { } issuer
               && issuer.Thumbprint != current.Thumbprint)
        {
            ordered.Add(issuer);
            remaining.Remove(issuer);
            current = issuer;
        }

        ordered.AddRange(remaining);
        return ordered;
    }

    private static CertificateFile Text(string fileName, string content) =>
        new(fileName, "application/x-pem-file", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)));

    private static void DisposeAll(X509Certificate2Collection collection)
    {
        foreach (var certificate in collection)
            certificate.Dispose();
    }
}

/// <summary>违反证书规则或业务状态，信息直接展示给后台用户。</summary>
public sealed class CertificateRuleException : Exception
{
    public CertificateRuleException(string message)
        : base(message)
    {
    }

    public CertificateRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
