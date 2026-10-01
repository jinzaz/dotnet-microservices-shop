using System.Security.Cryptography.X509Certificates;

namespace MSA.Shared;

/// <summary>
/// 生产环境 OpenIddict 签名 / 加密证书的唯一来源：环境变量。
/// 证书路径与口令属于部署机密，只由部署环境注入（compose、K8s Secret 等），不出现在 appsettings.json 里，
/// 也不会被配置文件里的值意外覆盖或兜底。认证中心与两个 BFF 以链接文件的方式共用这一份实现。
/// </summary>
internal static class OpenIddictCertificateEnvironment
{
    public const string PathVariable = "MSA_OPENIDDICT_CERT_PATH";
    public const string PasswordVariable = "MSA_OPENIDDICT_CERT_PASSWORD";

    /// <summary>
    /// 读取并校验证书。启动时就失败，比上线后第一次签发令牌时才失败好排查得多。
    /// </summary>
    public static X509Certificate2 Load()
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"The OpenIddict certificate is required outside Development: set {PathVariable} (pfx path) and {PasswordVariable}.");
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
