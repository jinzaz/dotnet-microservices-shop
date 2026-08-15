using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
#if RABBITMQ
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

    // RabbitMQ-backed EventBus. Enabled when RABBITMQ compilation symbol is defined and
    // RabbitMQ.Client package is available via central package management.
    public class RabbitMqEventBus : IEventBus, IDisposable
    {
        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly string _exchange;
        private readonly ConcurrentDictionary<string, List<EventHandlerDelegate>> _handlers = new();
        private readonly ConcurrentDictionary<string, string> _consumerTags = new();

        public RabbitMqEventBus(string hostName, string exchange = "events", int port = 5672)
        {
            _exchange = exchange;
            var factory = new ConnectionFactory() { HostName = hostName, Port = port };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_exchange, ExchangeType.Topic, durable: true);
        }

        public Task PublishAsync(IIntegrationEvent @event)
        {
            if (@event == null) return Task.CompletedTask;

            var body = Encoding.UTF8.GetBytes(@event.Payload ?? string.Empty);
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;

            if (@event.Headers != null && @event.Headers.Count > 0)
            {
                props.Headers = new Dictionary<string, object>();
                foreach (var h in @event.Headers)
                {
                    props.Headers[h.Key] = Encoding.UTF8.GetBytes(h.Value);
                }
            }

            var routingKey = @event.EventType ?? string.Empty;
            _channel.BasicPublish(_exchange, routingKey, props, body);
            return Task.CompletedTask;
        }

        public void Subscribe(string eventType, EventHandlerDelegate handler)
        {
            if (string.IsNullOrWhiteSpace(eventType) || handler == null) return;

            var list = _handlers.GetOrAdd(eventType, _ => new List<EventHandlerDelegate>());
            lock (list)
            {
                list.Add(handler);
                if (list.Count == 1)
                {
                    var queueName = _channel.QueueDeclare().QueueName;
                    _channel.QueueBind(queueName, _exchange, eventType);

                    var consumer = new EventingBasicConsumer(_channel);
                    consumer.Received += async (model, ea) =>
                    {
                        try
                        {
                            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
                            var headers = new Dictionary<string, string>();
                            if (ea.BasicProperties?.Headers != null)
                            {
                                foreach (var kv in ea.BasicProperties.Headers)
                                {
                                    try
                                    {
                                        if (kv.Value is byte[] bytes)
                                            headers[kv.Key] = Encoding.UTF8.GetString(bytes);
                                        else
                                            headers[kv.Key] = kv.Value?.ToString();
                                    }
                                    catch { }
                                }
                            }

                            var integration = new IntegrationEvent(ea.RoutingKey, payload, headers);

                            if (_handlers.TryGetValue(ea.RoutingKey, out var handlers))
                            {
                                var tasks = new List<Task>();
                                lock (handlers)
                                {
                                    foreach (var h in handlers)
                                    {
                                        try { tasks.Add(h(integration)); } catch { }
                                    }
                                }

                                await Task.WhenAll(tasks);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"[RabbitMqEventBus] Error handling message: {ex.Message}");
                        }
                    };

                    var consumerTag = _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
                    _consumerTags[eventType] = consumerTag;
                }
            }
        }

        public void Unsubscribe(string eventType, EventHandlerDelegate handler)
        {
            if (string.IsNullOrWhiteSpace(eventType) || handler == null) return;

            if (_handlers.TryGetValue(eventType, out var list))
            {
                lock (list)
                {
                    list.Remove(handler);
                    if (list.Count == 0)
                    {
                        if (_consumerTags.TryRemove(eventType, out var tag))
                        {
                            try { _channel.BasicCancel(tag); } catch { }
                        }
                        _handlers.TryRemove(eventType, out _);
                    }
                }
            }
        }

        public void Dispose()
        {
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
        }
    }
#else

    // Fallback placeholder implementation when RabbitMQ support is not enabled.
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
            var payload = JsonSerializer.Serialize(@event);
            System.Console.WriteLine($"[EventBus][Fallback] Publish to {_exchange} on {_host}: {payload}");
            return Task.CompletedTask;
        }

        public void Subscribe(string eventType, EventHandlerDelegate handler)
        {
            // no-op fallback
        }

        public void Unsubscribe(string eventType, EventHandlerDelegate handler)
        {
            // no-op fallback
        }
    }

#endif

}
