using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Certificates;
using MSA.CertificateService.Infrastructure;

namespace MSA.CertificateService.Application;

public sealed class TlsDeploymentOptions
{
    public const string SectionName = "Tls";

    /// <summary>
    /// nginx 读取证书的目录（容器内路径，宿主机目录同时以只读方式挂给 nginx）。每个站点一个子目录：
    /// &lt;目录&gt;/&lt;站点名&gt;/fullchain.pem 与 privkey.pem。为空时不部署（例如本地开发）。
    /// </summary>
    public string? DeployDirectory { get; set; }
}

/// <summary>
/// 把每个站点的当前 TLS 证书写成 nginx 用的 PEM 文件。内容没变就不写；写的时候先写临时文件再改名，
/// nginx 任何时候读到的都是完整文件。nginx 不会自己重新加载证书，宿主机上的定时 reload 负责让它生效（见 docker/README.md）。
/// </summary>
internal sealed partial class TlsDeployer(
    CertificateDbContext db,
    CertificateRegistry registry,
    CertificateKekRing keks,
    IOptions<TlsDeploymentOptions> options,
    ILogger<TlsDeployer> logger)
{
    public async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.DeployDirectory;
        if (string.IsNullOrWhiteSpace(root))
            return 0;

        var active = (await registry.ListAsync(CertificateConsumers.Edge, CertificatePurposes.TlsCode, includeInactive: false, cancellationToken))
            .Where(certificate => certificate.Stage == CertificateStage.Active)
            .ToList();
        var kek = keks.For(CertificateConsumers.Edge);
        var written = 0;
        foreach (var view in active)
        {
            var certificate = await db.Certificates.AsNoTracking().SingleAsync(item => item.Id == view.Id, cancellationToken);
            var pkcs8 = kek.Decrypt(certificate.PrivateKey, CertificateKek.CertificateContext(certificate.Id, certificate.Consumer, certificate.Purpose));
            try
            {
                var directory = Path.Combine(root, certificate.Slot);
                Directory.CreateDirectory(directory);
                var changed = await WriteIfChangedAsync(Path.Combine(directory, "fullchain.pem"), certificate.CertificatePem, secret: false, cancellationToken);
                changed |= await WriteIfChangedAsync(Path.Combine(directory, "privkey.pem"), PemEncoding.WriteString("PRIVATE KEY", pkcs8), secret: true, cancellationToken);
                if (changed)
                {
                    written++;
                    LogDeployed(certificate.Slot, directory, certificate.NotAfter);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }
        }

        return written;
    }

    private static async Task<bool> WriteIfChangedAsync(string path, string content, bool secret, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == content)
            return false;

        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        if (secret && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deployed the TLS certificate of site {Slot} to {Directory} (valid until {NotAfter}); nginx picks it up on its next reload.")]
    private partial void LogDeployed(string slot, string directory, DateTimeOffset notAfter);
}
