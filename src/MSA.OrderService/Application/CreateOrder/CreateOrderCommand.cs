using MSA.Infrastrucute.Mediator;
namespace MSA.OrderService.Application.CreateOrder;
public sealed record CreateOrderCommand(string ProductId, int Quantity, decimal Amount, string Currency) : ICommand<Guid>;
