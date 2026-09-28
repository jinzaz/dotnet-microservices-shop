using MSA.OrderService.Application;
using MSA.Persistence.Postgres;

namespace MSA.OrderService.Infrastructure;

internal sealed class PostgresOrderFulfillmentLock(OrderDbContext db) : IOrderFulfillmentLock
{
    /// <summary>同一订单的履约推进串行执行，避免两个管理员操作基于同一旧状态提交。</summary>
    public Task AcquireAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        return db.AcquireTransactionLockAsync($"order-fulfillment:{orderId:N}", cancellationToken);
    }
}
