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
        modelBuilder.Entity<OutboxMessage>(e => { e.ToTable("outbox_messages"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.Payload).HasColumnType("jsonb"); e.Property(x => x.LastError).HasMaxLength(4000); e.HasIndex(x => new { x.Status, x.NextAttemptOnUtc, x.LockedUntilUtc }); });
        modelBuilder.Entity<SagaState>(e => { e.ToTable("saga_states"); e.HasKey(x => x.CorrelationId); e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.Data).HasColumnType("jsonb"); e.Property(x => x.SagaType).HasMaxLength(200); e.Property(x => x.State).HasMaxLength(100); e.Property(x => x.FailureReason).HasMaxLength(4000); e.HasIndex(x => new { x.Status, x.TimeoutOnUtc, x.TimeoutLockedUntilUtc }); });
        modelBuilder.Entity<InboxMessage>(e => { e.ToTable("inbox_messages"); e.HasKey(x => new { x.MessageId, x.Consumer }); e.Property(x => x.Consumer).HasMaxLength(300); });
    }
    public async Task<bool> TryBeginMessageAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        // The inbox entry is the consumer's lease for a message.  AnyAsync + Add has a
        // race between replicas and is particularly dangerous when a handler performs
        // an external side effect. PostgreSQL can make the decision atomically and the
        // surrounding local transaction keeps it consistent with business data/outbox.
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
