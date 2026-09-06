using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.EventBusOutbox;
using MSA.Infrastrucute;
using MSA.Saga;
namespace MSA.Repositories.Persistence;

public abstract class MessagingDbContext(DbContextOptions options, IMediator mediator)
    : MSADbContext(options, mediator), IOutboxStore, ISagaStore
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
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
        });

        modelBuilder.Entity<SagaState>(entity =>
        {
            entity.ToTable("saga_states");
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
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(message => new { message.MessageId, message.Consumer });
            entity.Property(message => message.MessageId).HasComment("已消费的集成事件标识");
            entity.Property(message => message.Consumer).HasMaxLength(300).HasComment("消费处理器的稳定名称");
            entity.Property(message => message.ProcessedOnUtc).HasComment("首次成功登记消费的 UTC 时间");
        });
    }
    public async Task<bool> TryBeginMessageAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        // Inbox 记录既是消费凭证也是幂等闸门；利用数据库唯一键原子抢占，避免多副本先查后写的竞态。
        // 该写入与业务数据、Outbox 共用本地事务，确保“已消费”不会领先于业务结果提交。
        if (HasActiveTransaction && Database.IsNpgsql())
        {
            var affected = await Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inbox_messages ("MessageId", "Consumer", "ProcessedOnUtc")
                VALUES ({messageId}, {consumer}, {DateTimeOffset.UtcNow})
                ON CONFLICT ("MessageId", "Consumer") DO NOTHING;
                """, cancellationToken);
            return affected == 1;
        }

        if (await InboxMessages.AnyAsync(
                message => message.MessageId == messageId && message.Consumer == consumer,
                cancellationToken))
        {
            return false;
        }

        InboxMessages.Add(new InboxMessage(messageId, consumer, DateTimeOffset.UtcNow));
        return true;
    }

    void IOutboxStore.Add(OutboxMessage message) => OutboxMessages.Add(message);

    async Task<IReadOnlyList<OutboxMessage>> IOutboxStore.ClaimPendingAsync(
        Guid lockId,
        int batchSize,
        int maxRetries,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (!Database.IsNpgsql())
        {
            var fallbackRows = await OutboxMessages
                .Where(message =>
                    message.RetryCount < maxRetries
                    && message.NextAttemptOnUtc <= now
                    && (message.Status == OutboxStatus.Pending
                        || message.Status == OutboxStatus.Processing && message.LockedUntilUtc < now))
                .OrderBy(message => message.OccurredOnUtc)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            foreach (var row in fallbackRows)
                row.Claim(lockId, now.Add(duration));
            await base.SaveChangesAsync(cancellationToken);
            return fallbackRows;
        }

        // SKIP LOCKED 让多个发布实例分摊待处理消息，租约则允许崩溃实例的消息在超时后重新领取。
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var rows = await OutboxMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM outbox_messages
                WHERE "RetryCount" < {maxRetries}
                  AND "NextAttemptOnUtc" <= {now}
                  AND (
                    "Status" = {(int)OutboxStatus.Pending}
                    OR ("Status" = {(int)OutboxStatus.Processing} AND "LockedUntilUtc" < {now})
                  ) 
                ORDER BY "OccurredOnUtc", "Id"
                FOR UPDATE SKIP LOCKED
                LIMIT {batchSize}
                """)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.Claim(lockId, now.Add(duration));

        await base.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    Task IOutboxStore.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
    void ISagaStore.Add(SagaState state) => SagaStates.Add(state);
    Task<SagaState?> ISagaStore.FindAsync(Guid id, CancellationToken ct) => SagaStates.SingleOrDefaultAsync(x => x.CorrelationId == id, ct);

    async Task<IReadOnlyList<SagaState>> ISagaStore.ClaimDueTimeoutsAsync(
        Guid lockId,
        int batchSize,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (!Database.IsNpgsql())
        {
            var fallbackRows = await SagaStates
                .Where(saga =>
                    (saga.Status == SagaStatus.Running || saga.Status == SagaStatus.Compensating)
                    && saga.TimeoutOnUtc <= now
                    && (saga.TimeoutLockId == null || saga.TimeoutLockedUntilUtc < now))
                .OrderBy(saga => saga.TimeoutOnUtc)
                .ThenBy(saga => saga.CorrelationId)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            foreach (var row in fallbackRows)
                row.ClaimTimeout(lockId, now.Add(duration), now);
            await base.SaveChangesAsync(cancellationToken);
            return fallbackRows;
        }

        // Saga 超时任务沿用相同的租约模型，保证同一流程不会被多个实例并发补偿。
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var rows = await SagaStates
            .FromSqlInterpolated($"""
                SELECT *
                FROM saga_states
                WHERE "Status" IN ({(int)SagaStatus.Running}, {(int)SagaStatus.Compensating})
                  AND "TimeoutOnUtc" <= {now}
                  AND ("TimeoutLockId" IS NULL OR "TimeoutLockedUntilUtc" < {now})
                ORDER BY "TimeoutOnUtc", "CorrelationId"
                FOR UPDATE SKIP LOCKED
                LIMIT {batchSize}
                """)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.ClaimTimeout(lockId, now.Add(duration), now);

        await base.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    Task ISagaStore.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

    async Task<ISagaTransaction> ISagaStore.BeginTransactionAsync(CancellationToken cancellationToken) =>
        new SagaTransaction(
            this,
            await BeginTransactionAsync(cancellationToken));

    private sealed class SagaTransaction(
        MessagingDbContext owner,
        IDbContextTransaction transaction) : ISagaTransaction
    {
        private bool _committed;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await owner.CommitTransactionAsync(transaction, cancellationToken);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed && owner.HasActiveTransaction)
                await owner.RollbackTransactionAsync();
        }
    }
}
