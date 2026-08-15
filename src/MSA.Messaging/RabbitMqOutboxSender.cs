using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    // Placeholder sender — currently logs to console instead of publishing to RabbitMQ.
    public class RabbitMqOutboxSender
    {
        private readonly string _host;

        public RabbitMqOutboxSender(string hostName, int port = 5672)
        {
            _host = hostName;
        }

        public Task SendAsync<T>(T message, string exchange, string routingKey)
        {
            var payload = JsonSerializer.Serialize(message);
            System.Console.WriteLine($"[OutboxSender] Send to {exchange} on {_host}: {payload}");
            return Task.CompletedTask;
        }
    }
}
