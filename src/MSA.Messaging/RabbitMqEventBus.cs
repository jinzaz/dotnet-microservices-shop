using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Lightweight, in-process EventBus placeholder. Replace with RabbitMQ implementation when ready.
    public class RabbitMqEventBus : IEventBus
    {
        private readonly string _host;
        private readonly string _exchange;

        public RabbitMqEventBus(string hostName, string exchange = "events", int port = 5672)
        {
            _host = hostName;
            _exchange = exchange;
        }

        public Task PublishAsync(IIntegrationEvent @event)
        {
            // For now, just write to console for local development and testing.
            var payload = JsonSerializer.Serialize(@event);
            System.Console.WriteLine($"[EventBus] Publish to {_exchange} on {_host}: {payload}");
            return Task.CompletedTask;
        }
    }
}
