using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Domain;
namespace MSA.OrderService.Infrastructure;

internal sealed class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public void Add(Order order) => dbContext.Orders.Add(order);

    public Task<Order?> GetWithFulfillmentHistoryAsync(
        Guid orderId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .Include(order => order.FulfillmentStatusHistory)
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
}
