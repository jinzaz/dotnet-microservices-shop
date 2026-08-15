using Microsoft.AspNetCore.Mvc;
using MSA.Libraries.Messaging;
using System.Text.Json;

namespace MSA.OrderService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IOutboxRepository _outbox;

        public OrdersController(IOutboxRepository outbox)
        {
            _outbox = outbox;
        }

        public class CreateOrderRequest
        {
            public string ProductId { get; set; }
            public int Quantity { get; set; }
            public decimal Amount { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
        {
            // In real app: create order in business DB within same transaction as Outbox write.

            var correlationId = System.Guid.NewGuid().ToString("D");

            var payload = JsonSerializer.Serialize(new { OrderId = System.Guid.NewGuid().ToString("D"), req.ProductId, req.Quantity, req.Amount });

            var outboxMsg = new OutboxMessage
            {
                Id = System.Guid.NewGuid().ToString("D"),
                MessageId = System.Guid.NewGuid().ToString("D"),
                EventType = "OrderCreated",
                AggregateType = "Order",
                Payload = payload,
                Headers = new System.Collections.Generic.Dictionary<string, string> { { "CorrelationId", correlationId } },
                Sent = false,
                Retries = 0,
                CreatedAt = System.DateTime.UtcNow
            };

            await _outbox.AddAsync(outboxMsg);

            // Return 202 Accepted to indicate asynchronous processing
            return Accepted(new { outboxId = outboxMsg.Id, correlationId });
        }
    }
}
