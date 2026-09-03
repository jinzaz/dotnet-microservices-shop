using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Infrastructure;
namespace MSA.OrderService.Application.GetOrder;
internal sealed class GetOrderQueryHandler(OrderDbContext dbContext) : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken) => dbContext.Orders.AsNoTracking().Where(x => x.Id == request.OrderId).Select(x => new OrderDto(x.Id, x.ProductId, x.Quantity, x.Amount, x.Currency, x.CreatedOnUtc)).SingleOrDefaultAsync(cancellationToken);
}
