using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSA.EventBus.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MSA.EventBusRabbitMQ;

/// <summary>
/// 基于 RabbitMQ 的 IEventBus 实现。
/// - 发布：声明 Exchange/死信队列后 BasicPublish，routing key = 事件类型名
/// - 订阅：为每个订阅事件类型将 QueueName 绑定到 routing key，并启动一个共享的消费者
/// - 失败处理：处理异常时 Nack(requeue:false)，消息进入死信队列，避免无限重试阻塞队列
/// </summary>
public class RabbitMQEventBus : IEventBus, IAsyncDisposable, IHostedService
{

    private readonly ResiliencePipeline _pipeline;

    private readonly TextMapPropagator _propagator;
    private readonly ActivitySource _activitySource;

    private readonly IRabbitMQPersistentConnection _persistentConnection;
    private readonly ILogger<RabbitMQEventBus> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMQOptions _options;

    private readonly EventBusSubscriptionInfo _subscriptionInfo;

    private IChannel? _consumerChannel;

    public RabbitMQEventBus(
        IRabbitMQPersistentConnection persistentConnection,
        ILogger<RabbitMQEventBus> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMQOptions> options,
        IOptions<EventBusSubscriptionInfo> subscriptionInfo,
        RabbitMQTelemetry rabbitMQTelemetry)
    {
        _persistentConnection = persistentConnection;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _subscriptionInfo = subscriptionInfo.Value;
        _pipeline = CreateResiliencePipeline(options.Value.RetryCount);
        _propagator = rabbitMQTelemetry.Propagator;
        _activitySource = rabbitMQTelemetry.ActivitySource;

    }

    public async Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        if (!_persistentConnection.IsConnected) await _persistentConnection.TryConnectAsync(cancellationToken);

        await using var channel = await _persistentConnection.CreateChannelAsync(cancellationToken);
        await DeclareTopologyAsync(channel, cancellationToken);

        var routingKey = @event.EventType;
        var body = SerializeMessage(@event);

        var activityName = $"{routingKey} publish";

        await _pipeline.Execute(async () =>
        {
            using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client);

            // Depending on Sampling (and whether a listener is registered or not), the activity above may not be created.
            // If it is created, then propagate its context. If it is not created, the propagate the Current context, if any.

            ActivityContext contextToInject = default;

            if (activity != null)
            {
                contextToInject = activity.Context;
            }
            else if (Activity.Current != null)
            {
                contextToInject = Activity.Current.Context;
            }

            var properties = new BasicProperties()
            {
                DeliveryMode = DeliveryModes.Persistent
            };

            static void InjectTraceContextIntoBasicProperties(IBasicProperties props, string key, string value)
            {
                props.Headers ??= new Dictionary<string, object?>();
                props.Headers[key] = value;
            }

            _propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), properties, InjectTraceContextIntoBasicProperties);

            SetActivityContext(activity, routingKey, "publish");

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);
            }

            try
            {

                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetExceptionTags(ex);
                throw;
            }

        });
    }

    private static void SetActivityContext(Activity? activity, string routingKey, string operation)
    {
        if (activity is not null)
        {
            // These tags are added demonstrating the semantic conventions of the OpenTelemetry messaging specification
            // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination_kind", "queue");
            activity.SetTag("messaging.operation", operation);
            activity.SetTag("messaging.destination.name", routingKey);
            activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
        }
    }

    private async Task DoDeclareTopologyAsync()
    {
        if (!_persistentConnection.IsConnected) await _persistentConnection.TryConnectAsync();

        await using var channel = await _persistentConnection.CreateChannelAsync();
        await DeclareTopologyAsync(channel);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(_options.ExchangeName, _options.ExchangeType, durable: true, cancellationToken: cancellationToken);

        // 死信拓扑：处理失败的消息最终落到这里，可接入告警或人工重放工具
        await channel.ExchangeDeclareAsync(_options.DeadLetterExchange, "fanout", durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(_options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(_options.DeadLetterQueue, _options.DeadLetterExchange, routingKey: "", cancellationToken: cancellationToken);

        var queueArgs = new Dictionary<string, object?>
        {
            { "x-dead-letter-exchange", _options.DeadLetterExchange }
        };

        await channel.QueueDeclareAsync(_options.QueueName, durable: _options.DurableQueue, exclusive: false,
            autoDelete: false, arguments: queueArgs!, cancellationToken: cancellationToken);
    }

    private async Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs eventArgs)
    {
        static IEnumerable<string> ExtractTraceContextFromBasicProperties(IReadOnlyBasicProperties props, string key)
        {
            if (props.Headers is not null && props.Headers.TryGetValue(key, out var value) && value is byte[] bytes)
            {
                return [Encoding.UTF8.GetString(bytes)];
            }
            return [];
        }


        // Extract the PropagationContext of the upstream parent from the message headers.
        var parentContext = _propagator.Extract(default, eventArgs.BasicProperties, ExtractTraceContextFromBasicProperties);
        Baggage.Current = parentContext.Baggage;

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md
        var activityName = $"{eventArgs.RoutingKey} receive";

        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Client, parentContext.ActivityContext);

        SetActivityContext(activity, eventArgs.RoutingKey, "receive");

        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            activity?.SetTag("message", message);

            if (message.Contains("throw-fake-exception", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new InvalidOperationException($"Fake exception requested: \"{message}\"");
            }

            await ProcessEvent(eventName, message); //消息处理需要幂等和重试设计
            await _consumerChannel!.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理事件 '{EventName}' 失败，消息将进入死信队列: {Message}", eventName, message);

            activity?.SetExceptionTags(ex);
            await _consumerChannel!.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();

        if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
        {
            _logger.LogWarning("Unable to resolve event type for event name {EventName}", eventName);
            return;
        }

        // Deserialize the event
        var integrationEvent = DeserializeMessage(message, eventType);

        // REVIEW: This could be done in parallel

        // Get all the handlers using the event type as the key
        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
        {
            await handler.HandleAsync(integrationEvent);
        }




        //if (!_subsManager.HasSubscriptionsForEvent(eventName))
        //{
        //    _logger.LogWarning("没有找到事件 '{EventName}' 的订阅者，消息被忽略", eventName);
        //    return;
        //}

        //var eventType = _subsManager.GetEventTypeByName(eventName);
        //if (eventType is null) return;

        //var integrationEvent = (IntegrationEvent?)JsonSerializer.Deserialize(message, eventType);
        //if (integrationEvent is null) return;

        //using var scope = _scopeFactory.CreateScope();

        //foreach (var handlerType in _subsManager.GetHandlersForEvent(eventName))
        //{
        //    var handler = scope.ServiceProvider.GetService(handlerType);
        //    if (handler is null) continue;

        //    var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        //    var handleMethod = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync));

        //    if (handleMethod is not null)
        //    {
        //        await (Task)handleMethod.Invoke(handler, new object[] { integrationEvent, CancellationToken.None })!;
        //    }
        //}
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Messaging is async so we don't need to wait for it to complete.
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                _logger.LogInformation("Starting RabbitMQ connection on a background thread");

                if (!_persistentConnection.IsConnected)
                {
                    await _persistentConnection.TryConnectAsync(cancellationToken);
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Creating RabbitMQ consumer channel");
                }

                _consumerChannel = await _persistentConnection.CreateChannelAsync();

                _consumerChannel.CallbackExceptionAsync += (sender, ea) =>
                {
                    _logger.LogWarning(ea.Exception, "Error with RabbitMQ consumer channel");
                    return Task.CompletedTask;
                };

                await DoDeclareTopologyAsync();

                await _consumerChannel.ExchangeDeclareAsync(_options.ExchangeName, _options.ExchangeType, durable: true, cancellationToken: cancellationToken);


                var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

                consumer.ReceivedAsync += Consumer_ReceivedAsync;

                foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                {
                    await _consumerChannel.QueueBindAsync(
                        queue: _options.QueueName,
                        exchange: _options.ExchangeName,
                        routingKey: eventName);
                }

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Starting RabbitMQ basic consume");
                }

                await _consumerChannel.BasicConsumeAsync(
                    queue: _options.QueueName,
                    autoAck: false,
                    consumer: consumer);


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting RabbitMQ connection");
            }
        },
        TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {

        if (_consumerChannel is not null)
            await _consumerChannel.DisposeAsync();

    }



    private static ResiliencePipeline CreateResiliencePipeline(int retryCount)
    {
        // See https://www.pollydocs.org/strategies/retry.html
        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<BrokerUnreachableException>().Handle<SocketException>(),
            MaxRetryAttempts = retryCount,
            DelayGenerator = (context) => ValueTask.FromResult(GenerateDelay(context.AttemptNumber))
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();

        static TimeSpan? GenerateDelay(int attempt)
        {
            return TimeSpan.FromSeconds(Math.Pow(2, attempt));
        }
    }


    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    private IntegrationEvent DeserializeMessage(string message, Type eventType)
    {
        return JsonSerializer.Deserialize(message, eventType, _subscriptionInfo.JsonSerializerOptions) as IntegrationEvent
            ?? throw new JsonException($"Message payload cannot be deserialized as {eventType.FullName}.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    private byte[] SerializeMessage(IntegrationEvent @event)
    {
        return JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), _subscriptionInfo.JsonSerializerOptions);
    }
}
