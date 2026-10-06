using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MSA.Certificates;

/// <summary>
/// 证书库的存储格式：证书链存 PEM（叶子在前），私钥存 PKCS#8 再用 KEK 加密。
/// 这里是 PEM / PKCS#8 与 <see cref="X509Certificate2"/> 之间的互转，证书服务和各使用方共用。
/// </summary>
public static class CertificateMaterial
{
    private const string RsaOid = "1.2.840.113549.1.1.1";
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const string P384Oid = "1.3.132.0.34";

    /// <summary>按出现顺序读出 PEM 里的全部证书；第一张是叶子。</summary>
    public static X509Certificate2Collection ReadChain(string chainPem)
    {
        var chain = new X509Certificate2Collection();
        chain.ImportFromPem(chainPem);
        return chain.Count > 0 ? chain : throw new CryptographicException("The PEM contains no certificate.");
    }

    public static string ToPem(IEnumerable<X509Certificate2> chain) =>
        string.Concat(chain.Select(certificate => certificate.ExportCertificatePem() + "\n"));

    /// <summary>
    /// 给叶子证书装上私钥。请求体加密的私钥要做 ECDH 密钥协商，所以按 ECDiffieHellman 装载；
    /// 其余按公钥算法装 RSA / ECDSA。私钥和证书公钥不匹配时抛异常。
    /// </summary>
    public static X509Certificate2 WithPrivateKey(X509Certificate2 leaf, ReadOnlySpan<byte> pkcs8, string purpose)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (purpose == CertificatePurposes.BodyEncryptionCode)
        {
            using var ecdh = ECDiffieHellman.Create();
            ecdh.ImportPkcs8PrivateKey(pkcs8, out _);
            return leaf.CopyWithPrivateKey(ecdh);
        }

        switch (leaf.GetKeyAlgorithm())
        {
            case RsaOid:
                using (var rsa = RSA.Create())
                {
                    rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                    return leaf.CopyWithPrivateKey(rsa);
                }
            case EcPublicKeyOid:
                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportPkcs8PrivateKey(pkcs8, out _);
                    return leaf.CopyWithPrivateKey(ecdsa);
                }
            default:
                throw new CryptographicException($"Unsupported certificate key algorithm {leaf.GetKeyAlgorithm()}.");
        }
    }

    /// <summary>导出叶子证书私钥的 PKCS#8；证书必须带私钥。</summary>
    public static byte[] ExportPkcs8(X509Certificate2 certificate)
    {
        AsymmetricAlgorithm key = (AsymmetricAlgorithm?)certificate.GetRSAPrivateKey()
            ?? (AsymmetricAlgorithm?)certificate.GetECDsaPrivateKey()
            ?? certificate.GetECDiffieHellmanPrivateKey()
            ?? throw new CryptographicException("The certificate has no exportable private key.");
        using (key)
            return key.ExportPkcs8PrivateKey();
    }

    public static CertificateKeyTypes KeyTypeOf(X509Certificate2 certificate)
    {
        switch (certificate.GetKeyAlgorithm())
        {
            case RsaOid:
                using (var rsa = certificate.GetRSAPublicKey())
                    return rsa is { KeySize: >= 2048 } ? CertificateKeyTypes.Rsa : CertificateKeyTypes.None;
            case EcPublicKeyOid:
                // 曲线 OID 在公钥参数里；直接读 DER，不依赖各平台 ECCurve.Oid 是否填了 Value。
                return CurveOid(certificate) switch
                {
                    P256Oid => CertificateKeyTypes.EcP256,
                    P384Oid => CertificateKeyTypes.EcP384,
                    _ => CertificateKeyTypes.None
                };
            default:
                return CertificateKeyTypes.None;
        }
    }

    /// <summary>给人看的密钥描述，例如 "RSA 3072"、"EC P-256"。</summary>
    public static string DescribeKey(X509Certificate2 certificate)
    {
        if (certificate.GetKeyAlgorithm() == RsaOid)
        {
            using var rsa = certificate.GetRSAPublicKey();
            return $"RSA {rsa?.KeySize}";
        }

        return CurveOid(certificate) switch
        {
            P256Oid => "EC P-256",
            P384Oid => "EC P-384",
            var oid => $"EC {oid}"
        };
    }

    /// <summary>SHA-256 指纹（小写十六进制）。</summary>
    public static string Thumbprint(X509Certificate2 certificate) =>
        certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();

    private static string? CurveOid(X509Certificate2 certificate)
    {
        try
        {
            return new AsnReader(certificate.GetKeyAlgorithmParameters(), AsnEncodingRules.DER).ReadObjectIdentifier();
        }
        catch (AsnContentException)
        {
            return null;
        }
    }
}
