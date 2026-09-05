using MSA.OrderService.Domain;
namespace MSA.OrderService.Infrastructure;

internal sealed class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public void Add(Order order) => dbContext.Orders.Add(order);
}
