using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Domain;
namespace MSA.OrderService.Infrastructure;

internal sealed class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public void Add(Order order) => dbContext.Orders.Add(order);

    public async Task<Order?> GetWithFulfillmentHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.HasActiveTransaction)
            throw new InvalidOperationException("Order fulfillment requires an active transaction.");

        // 与订单事件处理器锁定同一行，避免履约和订单状态变更用旧版本交叉提交。
        var order = (await dbContext.Orders
            .FromSqlInterpolated($"SELECT * FROM order_orders WHERE \"Id\" = {orderId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (order is not null)
            await dbContext.Entry(order).Collection(value => value.FulfillmentStatusHistory)
                .LoadAsync(cancellationToken);
        return order;
    }
}
