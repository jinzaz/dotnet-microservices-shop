using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace MSA.Libraries.Messaging
{
    public class RabbitMqEventBus : IEventBus
    {
        private readonly ConnectionFactory _factory;
        private readonly string _exchange;

        public RabbitMqEventBus(string hostName, string exchange = "events", int port = 5672)
        {
            _factory = new ConnectionFactory { HostName = hostName, Port = port };
            _exchange = exchange;
        }

        public Task PublishAsync(IIntegrationEvent @event)
        {
            using var connection = _factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(_exchange, ExchangeType.Topic, durable: true);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
            var props = channel.CreateBasicProperties();
            props.MessageId = @event.Id;
            props.Timestamp = new AmqpTimestamp((long)(@event.OccurredAt - System.DateTime.UnixEpoch).TotalSeconds);

            // carry headers (trace/correlation)
            props.Headers = new System.Collections.Generic.Dictionary<string, object>();
            if (@event.Headers != null)
            {
                foreach (var kv in @event.Headers)
                {
                    props.Headers[kv.Key] = kv.Value;
                }
            }

            // add current activity trace id if present
            var activity = System.Diagnostics.Activity.Current;
            if (activity != null)
            {
                props.Headers["traceparent"] = activity.Id;
                props.Headers["traceId"] = activity.TraceId.ToString();
            }

            channel.BasicPublish(exchange: _exchange, routingKey: @event.EventType, basicProperties: props, body: body);

            return Task.CompletedTask;
        }
    }
}
