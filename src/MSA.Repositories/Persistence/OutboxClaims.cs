using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;
using MSA.Persistence.Postgres;

namespace MSA.Repositories.Persistence;

/// <summary>
/// <see cref="IOutboxStore.ClaimPendingAsync"/> 的共享实现。不能继承 <see cref="MessagingDbContext"/> 的上下文
/// （如 Identity 的 IdentityDbContext）也用同一套抢占与租约规则，而不是各写一份。
/// </summary>
public static class OutboxClaims
{
    /// <summary>
    /// SKIP LOCKED 让多个发布实例分摊待发消息，租约则让崩溃实例的消息在超时后被别人接管。
    /// </summary>
    public static async Task<IReadOnlyList<OutboxMessage>> ClaimPendingOutboxAsync(
        this DbContext db,
        Guid lockId,
        int batchSize,
        int maxRetries,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var owned = db.Database.CurrentTransaction is null;
        var transaction = owned ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var rows = await db.Set<OutboxMessage>()
                .Where(message =>
                    message.RetryCount < maxRetries
                    && message.NextAttemptOnUtc <= now
                    && (message.Status == OutboxStatus.Pending
                        || message.Status == OutboxStatus.Processing && message.LockedUntilUtc < now))
                .OrderBy(message => message.OccurredOnUtc)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ForUpdateSkipLocked()
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
                row.Claim(lockId, now.Add(duration));

            await db.SaveChangesAsync(cancellationToken);
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
}
