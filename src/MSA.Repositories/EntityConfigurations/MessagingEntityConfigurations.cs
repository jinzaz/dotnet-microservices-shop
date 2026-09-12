using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.EventBusOutbox;
using MSA.Repositories.Persistence;
using MSA.Saga;

namespace MSA.Repositories.EntityConfigurations;

internal sealed class OutboxMessageConfiguration(string tablePrefix)
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable($"{tablePrefix}_outbox_messages");
        entity.HasKey(message => message.Id);
        entity.Property(message => message.EventType).HasComment("集成事件的契约类型名称");
        entity.Property(message => message.Payload).HasColumnType("jsonb").HasComment("待发布事件的 JSON 载荷");
        entity.Property(message => message.Status).HasComment("消息发布状态");
        entity.Property(message => message.RetryCount).HasComment("累计发布重试次数");
        entity.Property(message => message.NextAttemptOnUtc).HasComment("下一次允许尝试发布的 UTC 时间");
        entity.Property(message => message.LockId).HasComment("当前发布任务的租约标识");
        entity.Property(message => message.LockedUntilUtc).HasComment("发布租约到期的 UTC 时间");
        entity.Property(message => message.LastError).HasMaxLength(4000).HasComment("最近一次发布失败原因");
        entity.Property(message => message.Version).IsConcurrencyToken().HasComment("乐观并发版本号");
        entity.HasIndex(message => new { message.Status, message.NextAttemptOnUtc, message.LockedUntilUtc });
    }
}

internal sealed class SagaStateConfiguration(string tablePrefix)
    : IEntityTypeConfiguration<SagaState>
{
    public void Configure(EntityTypeBuilder<SagaState> entity)
    {
        entity.ToTable($"{tablePrefix}_saga_states");
        entity.HasKey(saga => saga.CorrelationId);
        entity.Property(saga => saga.CorrelationId).HasComment("跨服务流程的关联标识");
        entity.Property(saga => saga.SagaType).HasMaxLength(200).HasComment("Saga 流程类型");
        entity.Property(saga => saga.State).HasMaxLength(100).HasComment("当前业务步骤");
        entity.Property(saga => saga.Data).HasColumnType("jsonb").HasComment("流程恢复与补偿所需的状态快照");
        entity.Property(saga => saga.Status).HasComment("Saga 执行状态");
        entity.Property(saga => saga.TimeoutOnUtc).HasComment("触发超时处理的 UTC 时间");
        entity.Property(saga => saga.TimeoutLockId).HasComment("当前超时处理任务的租约标识");
        entity.Property(saga => saga.TimeoutLockedUntilUtc).HasComment("超时处理租约到期的 UTC 时间");
        entity.Property(saga => saga.FailureReason).HasMaxLength(4000).HasComment("流程失败或补偿失败原因");
        entity.Property(saga => saga.Version).IsConcurrencyToken().HasComment("乐观并发版本号");
        entity.HasIndex(saga => new { saga.Status, saga.TimeoutOnUtc, saga.TimeoutLockedUntilUtc });
    }
}

internal sealed class InboxMessageConfiguration(string tablePrefix)
    : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> entity)
    {
        entity.ToTable($"{tablePrefix}_inbox_messages");
        entity.HasKey(message => new { message.MessageId, message.Consumer });
        entity.Property(message => message.MessageId).HasComment("已消费的集成事件标识");
        entity.Property(message => message.Consumer).HasMaxLength(300).HasComment("消费处理器的稳定名称");
        entity.Property(message => message.ProcessedOnUtc).HasComment("首次成功登记消费的 UTC 时间");
    }
}
