using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.CertificateService.Domain;
using MSA.Infrastrucute;

namespace MSA.CertificateService.Infrastructure;

public sealed class CertificateDbContext(DbContextOptions<CertificateDbContext> options) : DbContext(options)
{
    public const string Schema = "pki";

    public DbSet<ManagedCertificate> Certificates => Set<ManagedCertificate>();
    public DbSet<AcmeSubscription> AcmeSubscriptions => Set<AcmeSubscription>();
    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
    public DbSet<AcmeHttpChallenge> AcmeHttpChallenges => Set<AcmeHttpChallenge>();
    public DbSet<CertificateAudit> Audits => Set<CertificateAudit>();
    public DbSet<DataProtectionKeyRecord> DataProtectionKeys => Set<DataProtectionKeyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CertificateDbContext).Assembly);
    }
}

public sealed class CertificateDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<CertificateDbContext>;

internal sealed class ManagedCertificateConfiguration : IEntityTypeConfiguration<ManagedCertificate>
{
    public void Configure(EntityTypeBuilder<ManagedCertificate> entity)
    {
        // 列名被 MSA.Certificates 的原生 SQL 直接引用（id、consumer、purpose、slot、activate_at、not_after、
        // certificate_pem、private_key、kek_id、revoked_at、updated_at），改名要同步。
        entity.ToTable("certificates", table => table.HasComment("证书库：各使用方的签名 / 加密 / TLS 证书，私钥用使用方 KEK 加密"));
        entity.HasKey(certificate => certificate.Id);
        entity.Property(certificate => certificate.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(certificate => certificate.Consumer).HasColumnName("consumer").HasMaxLength(32).HasComment("使用方：identity / bff-web / bff-bm / edge");
        entity.Property(certificate => certificate.Purpose).HasColumnName("purpose").HasMaxLength(32).HasComment("用途：oidc-signing / oidc-encryption / oidc-client / body-encryption / tls");
        entity.Property(certificate => certificate.Slot).HasColumnName("slot").HasMaxLength(64).HasComment("互相替换的一组证书；TLS 为站点名");
        entity.Property(certificate => certificate.Name).HasColumnName("name").HasMaxLength(120);
        entity.Property(certificate => certificate.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(16);
        entity.Property(certificate => certificate.Subject).HasColumnName("subject").HasMaxLength(500);
        entity.Property(certificate => certificate.Issuer).HasColumnName("issuer").HasMaxLength(500);
        entity.Property(certificate => certificate.Thumbprint).HasColumnName("thumbprint").HasMaxLength(64).HasComment("SHA-256 指纹");
        entity.Property(certificate => certificate.KeyAlgorithm).HasColumnName("key_algorithm").HasMaxLength(32);
        entity.Property(certificate => certificate.DnsNames).HasColumnName("dns_names");
        entity.Property(certificate => certificate.NotBefore).HasColumnName("not_before");
        entity.Property(certificate => certificate.NotAfter).HasColumnName("not_after");
        entity.Property(certificate => certificate.ActivateAt).HasColumnName("activate_at").HasComment("成为当前证书的时间；阶段按它现算，不存状态");
        entity.Property(certificate => certificate.RevokedAt).HasColumnName("revoked_at");
        entity.Property(certificate => certificate.RevokedReason).HasColumnName("revoked_reason").HasMaxLength(500);
        entity.Property(certificate => certificate.CertificatePem).HasColumnName("certificate_pem").HasComment("证书链 PEM，叶子在前");
        entity.Property(certificate => certificate.PrivateKey).HasColumnName("private_key").HasComment("KEK 加密后的 PKCS#8 私钥：nonce || 密文 || tag");
        entity.Property(certificate => certificate.KekId).HasColumnName("kek_id").HasMaxLength(16).HasComment("加密私钥所用 KEK 的指纹");
        entity.Property(certificate => certificate.Exportable).HasColumnName("exportable");
        entity.Property(certificate => certificate.AutoRotate).HasColumnName("auto_rotate");
        entity.Property(certificate => certificate.AcmeSubscriptionId).HasColumnName("acme_subscription_id");
        entity.Property(certificate => certificate.CreatedAt).HasColumnName("created_at");
        entity.Property(certificate => certificate.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        entity.Property(certificate => certificate.UpdatedAt).HasColumnName("updated_at");

        entity.HasIndex(certificate => new { certificate.Consumer, certificate.Purpose, certificate.Slot })
            .HasDatabaseName("ix_certificates_group");
        entity.HasIndex(certificate => new { certificate.Consumer, certificate.Purpose, certificate.Slot, certificate.Thumbprint })
            .IsUnique()
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ux_certificates_group_thumbprint");
    }
}

internal sealed class AcmeSubscriptionConfiguration : IEntityTypeConfiguration<AcmeSubscription>
{
    public void Configure(EntityTypeBuilder<AcmeSubscription> entity)
    {
        entity.ToTable("acme_subscriptions", table => table.HasComment("ACME 托管的 TLS 站点证书，定时自动续订"));
        entity.HasKey(subscription => subscription.Id);
        entity.Property(subscription => subscription.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(subscription => subscription.Slot).HasColumnName("slot").HasMaxLength(64);
        entity.Property(subscription => subscription.Domains).HasColumnName("domains");
        entity.Property(subscription => subscription.ChallengeType).HasColumnName("challenge_type").HasConversion<string>().HasMaxLength(16);
        entity.Property(subscription => subscription.DnsProvider).HasColumnName("dns_provider").HasMaxLength(32);
        entity.Property(subscription => subscription.DirectoryUrl).HasColumnName("directory_url").HasMaxLength(300);
        entity.Property(subscription => subscription.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
        entity.Property(subscription => subscription.Enabled).HasColumnName("enabled");
        entity.Property(subscription => subscription.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        entity.Property(subscription => subscription.LastError).HasColumnName("last_error").HasMaxLength(2000);
        entity.Property(subscription => subscription.LastAttemptAt).HasColumnName("last_attempt_at");
        entity.Property(subscription => subscription.LastSucceededAt).HasColumnName("last_succeeded_at");
        entity.Property(subscription => subscription.NextAttemptAt).HasColumnName("next_attempt_at").HasComment("失败退避后的下一次重试时间");
        entity.Property(subscription => subscription.FailureCount).HasColumnName("failure_count");
        entity.Property(subscription => subscription.RenewRequested).HasColumnName("renew_requested");
        entity.Property(subscription => subscription.LeaseUntil).HasColumnName("lease_until").HasComment("申请进行中的租约，防止多实例重复申请");
        entity.Property(subscription => subscription.CreatedAt).HasColumnName("created_at");
        entity.Property(subscription => subscription.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        entity.Property(subscription => subscription.UpdatedAt).HasColumnName("updated_at");
        entity.HasIndex(subscription => subscription.Slot).IsUnique().HasDatabaseName("ux_acme_subscriptions_slot");
    }
}

internal sealed class AcmeAccountConfiguration : IEntityTypeConfiguration<AcmeAccount>
{
    public void Configure(EntityTypeBuilder<AcmeAccount> entity)
    {
        entity.ToTable("acme_accounts");
        entity.HasKey(account => account.Id);
        entity.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(account => account.DirectoryUrl).HasColumnName("directory_url").HasMaxLength(300);
        entity.Property(account => account.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
        entity.Property(account => account.AccountUrl).HasColumnName("account_url").HasMaxLength(500);
        entity.Property(account => account.PrivateKey).HasColumnName("private_key").HasComment("edge KEK 加密后的 PKCS#8 账户私钥");
        entity.Property(account => account.KekId).HasColumnName("kek_id").HasMaxLength(16);
        entity.Property(account => account.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(account => new { account.DirectoryUrl, account.ContactEmail }).IsUnique().HasDatabaseName("ux_acme_accounts_directory_contact");
    }
}

internal sealed class AcmeHttpChallengeConfiguration : IEntityTypeConfiguration<AcmeHttpChallenge>
{
    public void Configure(EntityTypeBuilder<AcmeHttpChallenge> entity)
    {
        entity.ToTable("acme_http_challenges");
        entity.HasKey(challenge => challenge.Token);
        entity.Property(challenge => challenge.Token).HasColumnName("token").HasMaxLength(200);
        entity.Property(challenge => challenge.KeyAuthorization).HasColumnName("key_authorization").HasMaxLength(500);
        entity.Property(challenge => challenge.ExpiresAt).HasColumnName("expires_at");
    }
}

internal sealed class CertificateAuditConfiguration : IEntityTypeConfiguration<CertificateAudit>
{
    public void Configure(EntityTypeBuilder<CertificateAudit> entity)
    {
        entity.ToTable("certificate_audits", table => table.HasComment("证书操作审计：导入、生成、激活、吊销、导出私钥、ACME 申请"));
        entity.HasKey(audit => audit.Id);
        entity.Property(audit => audit.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(audit => audit.At).HasColumnName("at");
        entity.Property(audit => audit.Actor).HasColumnName("actor").HasMaxLength(200);
        entity.Property(audit => audit.Action).HasColumnName("action").HasMaxLength(64);
        entity.Property(audit => audit.CertificateId).HasColumnName("certificate_id");
        entity.Property(audit => audit.AcmeSubscriptionId).HasColumnName("acme_subscription_id");
        entity.Property(audit => audit.Detail).HasColumnName("detail").HasMaxLength(2000);
        entity.HasIndex(audit => audit.At).HasDatabaseName("ix_certificate_audits_at");
        entity.HasIndex(audit => audit.CertificateId).HasDatabaseName("ix_certificate_audits_certificate");
    }
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKeyRecord>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRecord> entity)
    {
        // 列名被 MSA.Certificates.CertificateDbXmlRepository 的原生 SQL 直接引用。
        entity.ToTable("data_protection_keys", table => table.HasComment("各使用方的 ASP.NET Core DataProtection 密钥环，密钥用使用方 KEK 加密"));
        entity.HasKey(key => key.Id);
        entity.Property(key => key.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        entity.Property(key => key.Application).HasColumnName("application").HasMaxLength(64);
        entity.Property(key => key.FriendlyName).HasColumnName("friendly_name").HasMaxLength(200);
        entity.Property(key => key.Xml).HasColumnName("xml");
        entity.Property(key => key.CreatedAt).HasColumnName("created_at");
        entity.HasIndex(key => key.Application).HasDatabaseName("ix_data_protection_keys_application");
    }
}
