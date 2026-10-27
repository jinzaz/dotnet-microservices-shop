using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.EventBusOutbox;
using MSA.Infrastrucute;
using MSA.Persistence.Postgres;
using MSA.Repositories.EntityConfigurations;
using MSA.Saga;
namespace MSA.Repositories.Persistence;

public abstract class MessagingDbContext(
    DbContextOptions options,
    IMediator mediator,
    string tablePrefix)
    : MSADbContext(options, mediator), IOutboxStore, ISagaStore, IMessagingOperations
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(tablePrefix));
        modelBuilder.ApplyConfiguration(new SagaStateConfiguration(tablePrefix));
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(tablePrefix));
    }
    /// <summary>
    /// Inbox 记录既是消费凭证也是幂等闸门：靠唯一键原子抢占，先查后插在并发下会双双通过。
    /// 该写入与业务数据、Outbox 共用本地事务，确保「已消费」不会领先于业务结果提交。
    /// </summary>
    public Task<bool> TryBeginMessageAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        return InboxMessages.InsertOnConflictDoNothingAsync(
            new InboxMessage(messageId, consumer, DateTimeOffset.UtcNow),
            cancellationToken: cancellationToken);
    }

    async Task<IReadOnlyList<FailedOutboxMessageDto>> IMessagingOperations.ListFailedOutboxAsync(
        int take,
        CancellationToken cancellationToken) =>
        await OutboxMessages
            .AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Failed)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(Math.Clamp(take, 1, 200))
            .Select(message => new FailedOutboxMessageDto(
                message.Id,
                message.EventType,
                message.RetryCount,
                message.OccurredOnUtc,
                message.LastError))
            .ToArrayAsync(cancellationToken);

    async Task<int> IMessagingOperations.RequeueFailedOutboxAsync(
        Guid? messageId,
        int max,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await OutboxMessages
            .Where(message => message.Status == OutboxStatus.Failed
                && (!messageId.HasValue || message.Id == messageId.Value))
            .OrderBy(message => message.OccurredOnUtc)
            .Take(Math.Clamp(max, 1, 500))
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            row.Requeue(now);
        await base.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    async Task<IReadOnlyList<StalledSagaDto>> IMessagingOperations.ListStalledSagasAsync(
        DateTimeOffset stalledBefore,
        int take,
        CancellationToken cancellationToken) =>
        await SagaStates
            .AsNoTracking()
            .Where(saga => saga.Status == SagaStatus.Failed
                || ((saga.Status == SagaStatus.Running || saga.Status == SagaStatus.Compensating)
                    && saga.UpdatedOnUtc < stalledBefore))
            .OrderBy(saga => saga.UpdatedOnUtc)
            .Take(Math.Clamp(take, 1, 200))
            .Select(saga => new StalledSagaDto(
                saga.CorrelationId,
                saga.SagaType,
                saga.State,
                saga.Status.ToString(),
                saga.UpdatedOnUtc,
                saga.FailureReason))
            .ToArrayAsync(cancellationToken);

    async Task<(int Outbox, int Inbox)> IMessagingOperations.PurgeProcessedMessagesAsync(
        DateTimeOffset outboxBefore,
        DateTimeOffset inboxBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, 10_000);
        // Inbox 的保留期必须长于消息可能的最大重投时间，否则重复消息会被当成新消息重新处理。
        // 先取一批 id 再删：ExecuteDelete 在各提供程序上对 ORDER BY/LIMIT 的支持并不一致。
        var outboxIds = await OutboxMessages
            .Where(message => message.Status == OutboxStatus.Published && message.ProcessedOnUtc < outboxBefore)
            .OrderBy(message => message.ProcessedOnUtc)
            .Take(take)
            .Select(message => message.Id)
            .ToArrayAsync(cancellationToken);
        var outbox = outboxIds.Length == 0
            ? 0
            : await OutboxMessages.Where(message => outboxIds.Contains(message.Id)).ExecuteDeleteAsync(cancellationToken);

        var inboxIds = await InboxMessages
            .Where(message => message.ProcessedOnUtc < inboxBefore)
            .OrderBy(message => message.ProcessedOnUtc)
            .Take(take)
            .Select(message => message.MessageId)
            .ToArrayAsync(cancellationToken);
        var inbox = inboxIds.Length == 0
            ? 0
            : await InboxMessages
                .Where(message => inboxIds.Contains(message.MessageId) && message.ProcessedOnUtc < inboxBefore)
                .ExecuteDeleteAsync(cancellationToken);
        return (outbox, inbox);
    }

    void IOutboxStore.Add(OutboxMessage message) => OutboxMessages.Add(message);

    Task<IReadOnlyList<OutboxMessage>> IOutboxStore.ClaimPendingAsync(
        Guid lockId,
        int batchSize,
        int maxRetries,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        this.ClaimPendingOutboxAsync(lockId, batchSize, maxRetries, now, duration, cancellationToken);

    Task IOutboxStore.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
    void ISagaStore.Add(SagaState state) => SagaStates.Add(state);
    Task<SagaState?> ISagaStore.FindAsync(Guid id, CancellationToken ct) => SagaStates.SingleOrDefaultAsync(x => x.CorrelationId == id, ct);

    /// <summary>Saga 超时任务沿用同一套租约模型，保证同一流程不会被多个实例并发补偿。</summary>
    async Task<IReadOnlyList<SagaState>> ISagaStore.ClaimDueTimeoutsAsync(
        Guid lockId,
        int batchSize,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var owned = !HasActiveTransaction;
        var transaction = owned ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var rows = await SagaStates
                .Where(saga =>
                    (saga.Status == SagaStatus.Running || saga.Status == SagaStatus.Compensating)
                    && saga.TimeoutOnUtc <= now
                    && (saga.TimeoutLockId == null || saga.TimeoutLockedUntilUtc < now))
                .OrderBy(saga => saga.TimeoutOnUtc)
                .ThenBy(saga => saga.CorrelationId)
                .Take(batchSize)
                .ForUpdateSkipLocked()
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
                row.ClaimTimeout(lockId, now.Add(duration), now);

            await base.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return rows;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
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
