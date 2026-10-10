using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
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
        var order = await dbContext.Orders
            .Where(value => value.Id == orderId)
            .ForUpdate()
            .SingleOrDefaultAsync(cancellationToken);
        if (order is not null)
            await dbContext.Entry(order).Collection(value => value.FulfillmentStatusHistory)
                .LoadAsync(cancellationToken);
        return order;
    }

    public Task<bool> HasOpenAfterSalesAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.AfterSalesRequests.AnyAsync(
            request => request.OrderId == orderId
                && request.Status != AfterSalesStatus.Rejected
                && request.Status != AfterSalesStatus.Cancelled
                && request.Status != AfterSalesStatus.Refunded
                && request.Status != AfterSalesStatus.Completed,
            cancellationToken);
}
