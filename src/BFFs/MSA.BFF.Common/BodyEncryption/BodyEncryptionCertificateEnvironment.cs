using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// 生产环境 body 加密密钥证书的唯一来源：环境变量。跟 OpenIddict 的签名/加密证书是两套独立密钥
/// （scripts/new-bodyenc-certs），变量名也不同，不会互相覆盖。开发环境没配置时随机生成一份，
/// 进程重启就变，不用管轮换。
/// </summary>
internal static class BodyEncryptionCertificateEnvironment
{
    public const string PathVariable = "MSA_BODYENC_CERT_PATH";
    public const string PasswordVariable = "MSA_BODYENC_CERT_PASSWORD";

    public static ECDiffieHellman Load(IWebHostEnvironment environment)
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            if (environment.IsDevelopment())
                return ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

            throw new InvalidOperationException(
                $"The body encryption certificate is required outside Development: set {PathVariable} (pfx path) and {PasswordVariable}.");
        }
        if (!File.Exists(path))
            throw new InvalidOperationException($"{PathVariable} points to '{path}', which does not exist.");

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            Environment.GetEnvironmentVariable(PasswordVariable),
            X509KeyStorageFlags.EphemeralKeySet);
        if (certificate.NotAfter <= DateTime.Now)
            throw new InvalidOperationException($"The certificate at {PathVariable} expired on {certificate.NotAfter:yyyy-MM-dd}.");

        return certificate.GetECDiffieHellmanPrivateKey()
            ?? throw new InvalidOperationException($"The certificate at {PathVariable} has no ECDH private key.");
    }
}
