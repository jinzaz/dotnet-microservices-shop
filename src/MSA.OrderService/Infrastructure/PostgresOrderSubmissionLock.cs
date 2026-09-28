using MSA.OrderService.Application;
using MSA.Persistence.Postgres;

namespace MSA.OrderService.Infrastructure;

internal sealed class PostgresOrderSubmissionLock(OrderDbContext db) : IOrderSubmissionLock
{
    /// <summary>下单按资源键串行化；键会去重并按固定顺序加锁，避免交叉等待。</summary>
    public Task AcquireAsync(IEnumerable<string> resourceKeys, CancellationToken cancellationToken) =>
        db.AcquireTransactionLocksAsync(resourceKeys, cancellationToken);
}
