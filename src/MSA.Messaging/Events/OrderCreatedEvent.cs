using System.Text.Json;

namespace MSA.Libraries.Messaging.Events
{
    public class OrderCreatedEvent : MSA.Libraries.Messaging.IntegrationEvent
    {
        public OrderCreatedEvent(string orderId, decimal amount, string correlationId = null)
            : base("OrderCreated", JsonSerializer.Serialize(new { OrderId = orderId, Amount = amount }),
                  new System.Collections.Generic.Dictionary<string, string>() { { "CorrelationId", correlationId ?? System.Guid.NewGuid().ToString("D") } })
        {
        }
    }
}
