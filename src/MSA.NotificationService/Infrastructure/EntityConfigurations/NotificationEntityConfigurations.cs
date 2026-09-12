using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.NotificationService.Domain;

namespace MSA.NotificationService.Infrastructure.EntityConfigurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.ToTable("notification_notifications");
        entity.HasKey(notification => notification.Id);
        entity.HasIndex(notification => notification.DeduplicationKey).IsUnique();
        entity.HasIndex(notification => new
        {
            notification.Status,
            notification.NextAttemptOnUtc,
            notification.LockedUntilUtc
        });
        entity.Property(notification => notification.DeduplicationKey).HasComment("业务通知去重键");
        entity.Property(notification => notification.Status).HasComment("通知投递状态");
        entity.Property(notification => notification.RetryCount).HasComment("累计投递重试次数");
        entity.Property(notification => notification.NextAttemptOnUtc).HasComment("下一次允许投递的 UTC 时间");
        entity.Property(notification => notification.LockId).HasComment("当前投递任务的租约标识");
        entity.Property(notification => notification.LockedUntilUtc).HasComment("投递租约到期的 UTC 时间");
        entity.Property(notification => notification.LastError).HasMaxLength(2000).HasComment("最近一次投递失败原因");
        entity.Property(notification => notification.Version).IsConcurrencyToken().HasComment("通知乐观并发版本号");
    }
}

internal sealed class NotificationTemplateConfiguration
    : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> entity)
    {
        entity.ToTable("notification_templates");
        entity.HasKey(template => template.Id);
        entity.HasIndex(template => template.Code).IsUnique();
        entity.Property(template => template.Code).HasComment("通知模板业务编码");
        entity.Property(template => template.IsActive).HasComment("模板是否可用于创建新通知");
    }
}

internal sealed class UserNotificationPreferenceConfiguration
    : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> entity)
    {
        entity.ToTable("notification_user_notification_preferences");
        entity.HasKey(preference => preference.Id);
        entity.HasIndex(preference => preference.UserId).IsUnique();
        entity.Property(preference => preference.UserId).HasComment("通知偏好所属用户标识");
        entity.Property(preference => preference.EmailEnabled).HasComment("是否允许邮件通知");
        entity.Property(preference => preference.SmsEnabled).HasComment("是否允许短信通知");
    }
}
