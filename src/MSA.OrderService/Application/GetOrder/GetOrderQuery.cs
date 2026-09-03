using MSA.Infrastrucute.Mediator;
namespace MSA.OrderService.Application.GetOrder;
public sealed record OrderDto(Guid Id, string ProductId, int Quantity, decimal Amount, string Currency, DateTimeOffset CreatedOnUtc);
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto?>;
