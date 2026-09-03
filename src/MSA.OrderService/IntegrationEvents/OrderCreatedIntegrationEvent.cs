using MSA.EventBus.Abstractions;

namespace MSA.OrderService.IntegrationEvents;

public sealed class OrderCreatedIntegrationEvent(Guid orderId, string productId, int quantity, decimal amount, string currency) : IntegrationEvent
{
    public Guid OrderId { get; } = orderId;
    public string ProductId { get; } = productId;
    public int Quantity { get; } = quantity;
    public decimal Amount { get; } = amount;
    public string Currency { get; } = currency;
}
