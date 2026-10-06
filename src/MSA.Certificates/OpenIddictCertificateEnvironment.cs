using System.Security.Cryptography.X509Certificates;

namespace MSA.Certificates;

/// <summary>
/// 证书库里还没有当前证书时的退路：生产环境从环境变量读 pfx（之前唯一的来源，迁移期间继续可用）。
/// 证书路径与口令属于部署机密，只由部署环境注入（compose、K8s Secret 等），不出现在 appsettings.json 里。
/// 同一张证书同时用于签名和加密；证书库里这两个用途是分开的两张证书。
/// </summary>
public static class OpenIddictCertificateEnvironment
{
    public const string PathVariable = "MSA_OPENIDDICT_CERT_PATH";
    public const string PasswordVariable = "MSA_OPENIDDICT_CERT_PASSWORD";

    private static readonly Lazy<X509Certificate2> Certificate = new(Read);

    /// <summary>读取并校验证书，进程内只读一次。启动时就失败，比上线后第一次签发令牌时才失败好排查得多。</summary>
    public static X509Certificate2 Load() => Certificate.Value;

    private static X509Certificate2 Read()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"The OpenIddict certificate is required outside Development: import one into the certificate store, " +
                $"or set {PathVariable} (pfx path) and {PasswordVariable}.");
        }
        if (!File.Exists(path))
            throw new InvalidOperationException($"{PathVariable} points to '{path}', which does not exist.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            Environment.GetEnvironmentVariable(PasswordVariable),
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException($"The certificate at {PathVariable} has no private key.");
        if (certificate.NotAfter <= DateTime.Now)
            throw new InvalidOperationException($"The certificate at {PathVariable} expired on {certificate.NotAfter:yyyy-MM-dd}.");

        return certificate;
    }
}
