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
            // 按消息语义约定标成 Producer/Consumer，链路视图里才会把发布和消费连成一条异步调用。
            using var activity = _activitySource.StartActivity(activityName, ActivityKind.Producer);

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
                DeliveryMode = DeliveryModes.Persistent,
                // 事件名随消息走：重试回流与死信重放都据此还原事件类型。
                Headers = new Dictionary<string, object?> { [EventNameHeader] = routingKey }
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

        // 重试队列：消息在这里等待 TTL 后由死信机制回到主队列，实现「延迟重投」而不占用消费者。
        // ponytail: 固定延迟 + 固定次数；需要更细的退避曲线时改成多级延迟队列。
        var retryArgs = new Dictionary<string, object?>
        {
            { "x-message-ttl", (int)_options.RetryDelay.TotalMilliseconds },
            { "x-dead-letter-exchange", string.Empty },
            { "x-dead-letter-routing-key", _options.QueueName }
        };
        await channel.QueueDeclareAsync(_options.RetryQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: retryArgs!, cancellationToken: cancellationToken);
    }

    /// <summary>事件名头：消息经重试队列回流后 RoutingKey 会变成队列名，必须靠它还原事件类型。</summary>
    internal const string EventNameHeader = "x-msa-event";

    /// <summary>已投递次数头，用于限制重试并最终进入死信队列。</summary>
    internal const string AttemptHeader = "x-msa-attempt";

    internal static string? ReadHeaderString(IReadOnlyBasicProperties properties, string key) =>
        properties.Headers is not null && properties.Headers.TryGetValue(key, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => value?.ToString()
            }
            : null;

    private static int ReadAttempt(IReadOnlyBasicProperties properties) =>
        properties.Headers is not null
        && properties.Headers.TryGetValue(AttemptHeader, out var value)
        && int.TryParse(value switch { byte[] bytes => Encoding.UTF8.GetString(bytes), _ => value?.ToString() }, out var attempt)
            ? attempt
            : 0;

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

        using var activity = _activitySource.StartActivity(activityName, ActivityKind.Consumer, parentContext.ActivityContext);

        SetActivityContext(activity, eventArgs.RoutingKey, "receive");

        // 回流的消息 RoutingKey 是队列名，事件类型以头部为准。
        var eventName = ReadHeaderString(eventArgs.BasicProperties, EventNameHeader) ?? eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            activity?.SetTag("message", message);

            // 处理器自身保证幂等（Inbox 去重），因此重复投递是安全的。
            await ProcessEvent(eventName, message);
            await _consumerChannel!.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            activity?.SetExceptionTags(ex);
            var attempt = ReadAttempt(eventArgs.BasicProperties) + 1;
            if (attempt < _options.MaxDeliveryAttempts)
            {
                _logger.LogWarning(ex, "处理事件 '{EventName}' 失败（第 {Attempt}/{MaxAttempts} 次），{Delay} 后重投",
                    eventName, attempt, _options.MaxDeliveryAttempts, _options.RetryDelay);
                await RetryLaterAsync(eventArgs, eventName, attempt);
                return;
            }

            _logger.LogError(ex, "处理事件 '{EventName}' 失败 {Attempt} 次，消息进入死信队列: {Message}", eventName, attempt, message);
            await _consumerChannel!.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false);
        }
    }

    /// <summary>
    /// 把消息投到重试队列后再确认原消息：重试队列的 TTL 到期后消息回到主队列。
    /// 先投递后确认，崩溃时最多重复投递一次，由消费端的 Inbox 去重兜底。
    /// </summary>
    private async Task RetryLaterAsync(BasicDeliverEventArgs eventArgs, string eventName, int attempt)
    {
        try
        {
            var headers = eventArgs.BasicProperties.Headers is null
                ? []
                : new Dictionary<string, object?>(eventArgs.BasicProperties.Headers);
            headers[EventNameHeader] = eventName;
            headers[AttemptHeader] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await _consumerChannel!.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.RetryQueue,
                mandatory: true,
                basicProperties: new BasicProperties { DeliveryMode = DeliveryModes.Persistent, Headers = headers },
                body: eventArgs.Body.ToArray());
            await _consumerChannel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception exception)
        {
            // 重投也失败时保持未确认状态，连接恢复后 broker 会重新投递，绝不静默丢消息。
            _logger.LogError(exception, "事件 '{EventName}' 重投失败，保留未确认状态等待重新投递", eventName);
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
                await _consumerChannel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: _options.PrefetchCount,
                    global: false,
                    cancellationToken: cancellationToken);


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
