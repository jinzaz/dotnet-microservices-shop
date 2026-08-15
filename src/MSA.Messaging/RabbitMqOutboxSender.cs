using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public class RabbitMqOutboxSender
    {
        private readonly ConnectionFactory _factory;

        public RabbitMqOutboxSender(string hostName, int port = 5672)
        {
            _factory = new ConnectionFactory { HostName = hostName, Port = port };
        }

        public Task SendAsync<T>(T message, string exchange, string routingKey)
        {
            using var connection = _factory.CreateConnection();
            using var channel = connection.CreateModel();

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            channel.BasicPublish(exchange: exchange, routingKey: routingKey, basicProperties: null, body: body);

            return Task.CompletedTask;
        }
    }
}
