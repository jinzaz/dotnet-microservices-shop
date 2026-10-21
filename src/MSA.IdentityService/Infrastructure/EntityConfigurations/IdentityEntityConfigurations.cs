using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.IdentityService.Domain;
using static MSA.Persistence.Postgres.CheckSql;

namespace MSA.IdentityService.Infrastructure.EntityConfigurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> entity)
    {
        entity.Property(user => user.DisplayName).HasComment("用户对外展示名称");
        entity.Property(user => user.IsActive).HasComment("账号是否允许登录和执行业务操作");
        entity.Property(user => user.CreatedOnUtc).HasComment("账号创建的 UTC 时间");
        entity.Property(user => user.LastLoginOnUtc).HasComment("最近一次成功登录的 UTC 时间");
        entity.Property(user => user.PhoneNumber).HasComment("用于账号安全验证的 E.164 手机号");
        entity.Property(user => user.PhoneNumberConfirmed).HasComment("手机号是否已完成归属验证");

        entity.HasIndex(user => user.PhoneNumber)
            .IsUnique()
            .HasDatabaseName("PhoneNumberIndex")
            .HasFilter("\"PhoneNumber\" IS NOT NULL AND \"PhoneNumberConfirmed\" = TRUE");
    }
}

internal sealed class SmsVerificationMessageConfiguration : IEntityTypeConfiguration<SmsVerificationMessage>
{
    public void Configure(EntityTypeBuilder<SmsVerificationMessage> entity)
    {
        entity.ToTable("identity_sms_messages", table =>
        {
            table.HasCheckConstraint("CK_identity_sms_messages_attempts", "\"VerificationAttempts\" >= 0");
            table.HasCheckConstraint(
                "CK_identity_sms_messages_timestamps",
                $"\"ExpiresOnUtc\" > \"CreatedOnUtc\" AND {NotBefore(nameof(SmsVerificationMessage.UpdatedOnUtc), nameof(SmsVerificationMessage.CreatedOnUtc))}");
        });

        entity.HasKey(message => message.Id);
        entity.Property(message => message.UserId).HasComment("关联的 ASP.NET Core Identity 用户 Id");
        entity.Property(message => message.PhoneNumber).HasMaxLength(16).HasComment("接收短信的 E.164 手机号");
        entity.Property(message => message.Purpose).HasConversion<int>().HasComment("验证码用途");
        entity.Property(message => message.TemplateKey).HasMaxLength(100).HasComment("与云厂商无关的逻辑模板键");
        entity.Property(message => message.ProtectedCode).HasMaxLength(2048).HasComment("经 Data Protection 加密的验证码载荷");
        entity.Property(message => message.Provider).HasMaxLength(100).HasComment("短信云服务商适配器名称");
        entity.Property(message => message.ProviderMessageId).HasMaxLength(500).HasComment("短信云服务商消息 Id");
        entity.Property(message => message.Status).HasConversion<int>().HasComment("发送与消费状态");
        entity.Property(message => message.VerificationAttempts).HasComment("已失败的验证码校验次数");
        entity.Property(message => message.FailureCode).HasMaxLength(100).HasComment("不包含敏感信息的发送失败代码");
        entity.Property(message => message.CreatedOnUtc).HasComment("验证码创建时间");
        entity.Property(message => message.UpdatedOnUtc).HasComment("记录最近更新时间");
        entity.Property(message => message.ExpiresOnUtc).HasComment("验证码失效时间");
        entity.Property(message => message.SentOnUtc).HasComment("短信服务商接受消息的时间");
        entity.Property(message => message.ConsumedOnUtc).HasComment("验证码成功消费时间");
        entity.Property(message => message.Version).IsConcurrencyToken().HasComment("乐观并发版本号");

        entity.HasIndex(message => new { message.PhoneNumber, message.Purpose, message.CreatedOnUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_identity_sms_messages_phone_purpose_created");
        entity.HasIndex(message => new { message.Status, message.ExpiresOnUtc })
            .HasDatabaseName("IX_identity_sms_messages_status_expires");
        entity.HasIndex(message => new { message.Provider, message.ProviderMessageId })
            .IsUnique()
            .HasFilter("\"ProviderMessageId\" IS NOT NULL")
            .HasDatabaseName("IX_identity_sms_messages_provider_message");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(message => message.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
