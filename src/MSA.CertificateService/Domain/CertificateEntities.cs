namespace MSA.CertificateService.Domain;

public enum CertificateSource
{
    Imported,
    Generated,
    Acme
}

/// <summary>
/// 证书库里的一张证书。阶段（待生效 / 当前 / 退役中…）不落库，按 <see cref="ActivateAt"/> 与同组其他证书现算，
/// 见 <see cref="MSA.Certificates.CertificateTimeline"/>。各使用方直接读这张表（MSA.Certificates.CertificateStoreLoader），
/// 改列名要两边一起改。
/// </summary>
public sealed class ManagedCertificate
{
    public Guid Id { get; init; }
    public required string Consumer { get; init; }
    public required string Purpose { get; init; }

    /// <summary>互相替换的一组证书。TLS 证书是站点名（也是部署目录名），其余用途固定为 default。</summary>
    public required string Slot { get; init; }

    public required string Name { get; set; }
    public CertificateSource Source { get; init; }
    public required string Subject { get; init; }
    public required string Issuer { get; init; }

    /// <summary>SHA-256 指纹，小写十六进制。</summary>
    public required string Thumbprint { get; init; }

    public required string KeyAlgorithm { get; init; }
    public required string[] DnsNames { get; init; }
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public DateTimeOffset ActivateAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    /// <summary>证书链 PEM，叶子在前。</summary>
    public required string CertificatePem { get; init; }

    /// <summary>PKCS#8 私钥，用使用方的 KEK 加密（附加数据绑定 Id、使用方、用途）。</summary>
    public required byte[] PrivateKey { get; init; }

    public required string KekId { get; init; }

    /// <summary>是否允许导出私钥。只能从允许改成禁止。</summary>
    public bool Exportable { get; set; }

    /// <summary>到期前（或满轮换周期）自动生成接替证书。TLS 证书由 ACME 托管续订，不看这个开关。</summary>
    public bool AutoRotate { get; set; }

    public Guid? AcmeSubscriptionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required string CreatedBy { get; init; }

    /// <summary>每次改动都要更新：使用方靠"行数 + 最新更新时间"判断要不要重新加载。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AcmeChallengeType
{
    /// <summary>TXT 记录验证，普通域名和通配符域名都能用，需要 DNS 服务商 API。</summary>
    Dns01,

    /// <summary>HTTP 文件验证，只能用于普通域名，需要 80 端口的 /.well-known/acme-challenge/ 转发到证书服务。</summary>
    Http01
}

public enum AcmeSubscriptionStatus
{
    Pending,
    Succeeded,
    Failed
}

/// <summary>
/// 一个由 ACME 托管的 TLS 站点证书：定时任务在证书剩余寿命不足 1/3 时自动重新申请，
/// 新证书立即成为该站点（<see cref="Slot"/>）的当前证书并写到 nginx 的证书目录。
/// </summary>
public sealed class AcmeSubscription
{
    public Guid Id { get; init; }

    /// <summary>站点名，即 TLS 证书的槽位与部署目录名。</summary>
    public required string Slot { get; init; }

    public required string[] Domains { get; set; }
    public AcmeChallengeType ChallengeType { get; set; }
    public string? DnsProvider { get; set; }
    public required string DirectoryUrl { get; set; }
    public required string ContactEmail { get; set; }
    public bool Enabled { get; set; }
    public AcmeSubscriptionStatus Status { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }

    /// <summary>失败后的下一次重试时间（指数退避）。</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public int FailureCount { get; set; }

    /// <summary>后台点了"立即续订"，或域名改了。</summary>
    public bool RenewRequested { get; set; }

    /// <summary>申请进行中的租约：多个实例同时跑定时任务时只有拿到租约的那个去申请。</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>ACME 账户（按 CA 目录地址 + 联系邮箱区分）。私钥用 edge 的 KEK 加密。</summary>
public sealed class AcmeAccount
{
    public Guid Id { get; init; }
    public required string DirectoryUrl { get; init; }
    public required string ContactEmail { get; init; }
    public required string AccountUrl { get; init; }
    public required byte[] PrivateKey { get; init; }
    public required string KekId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>进行中的 HTTP-01 验证：CA 来取 /.well-known/acme-challenge/{token} 时返回 key authorization。</summary>
public sealed class AcmeHttpChallenge
{
    public required string Token { get; init; }
    public required string KeyAuthorization { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class CertificateAudit
{
    public Guid Id { get; init; }
    public DateTimeOffset At { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public Guid? CertificateId { get; init; }
    public Guid? AcmeSubscriptionId { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// 各使用方的 DataProtection 密钥环（MSA.Certificates.CertificateDbXmlRepository 用原生 SQL 读写）。
/// 证书服务只负责建表，不读写它。
/// </summary>
public sealed class DataProtectionKeyRecord
{
    public long Id { get; init; }
    public required string Application { get; init; }
    public required string FriendlyName { get; init; }
    public required string Xml { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
