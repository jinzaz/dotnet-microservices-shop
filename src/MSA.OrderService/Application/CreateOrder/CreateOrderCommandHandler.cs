using MediatR;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;
using MSA.OrderService.IntegrationEvents;
using System.Text.Json;
namespace MSA.OrderService.Application.CreateOrder;
internal sealed class CreateOrderCommandHandler(IOrderRepository orders, IOutboxStore outbox) : IRequestHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order(Guid.NewGuid(), request.ProductId, request.Quantity, request.Amount, request.Currency);
        var integrationEvent = new OrderCreatedIntegrationEvent(order.Id, order.ProductId, order.Quantity, order.Amount, order.Currency);
        orders.Add(order);
        outbox.Add(OutboxMessage.Create(integrationEvent.GetType().AssemblyQualifiedName!, JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
        return Task.FromResult(order.Id);
    }
}
