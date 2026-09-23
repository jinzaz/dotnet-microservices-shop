using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSA.EventBus.Abstractions;
using RabbitMQ.Client;

namespace MSA.EventBusRabbitMQ;

/// <summary>
/// 读取与重放本服务死信队列的实现。重放会把消息按原事件名重新投到主交换机，
/// 因此修复根因（例如下游服务恢复）之后可以一次性把积压的消息放回主流程。
/// </summary>
internal sealed class RabbitMQDeadLetterAdmin(
    IRabbitMQPersistentConnection connection,
    IOptions<RabbitMQOptions> options,
    ILogger<RabbitMQDeadLetterAdmin> logger) : IDeadLetterAdmin
{
    private readonly RabbitMQOptions _options = options.Value;

    public async Task<IReadOnlyList<DeadLetterMessage>> PeekAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var channel = await OpenChannelAsync(cancellationToken);
        var messages = new List<DeadLetterMessage>();
        var deliveryTags = new List<ulong>();
        for (var index = 0; index < Math.Clamp(max, 1, 200); index++)
        {
            var result = await channel.BasicGetAsync(_options.DeadLetterQueue, autoAck: false, cancellationToken);
            if (result is null)
                break;
            deliveryTags.Add(result.DeliveryTag);
            messages.Add(Describe(result.BasicProperties, result.RoutingKey, result.Body.Span));
        }

        // 只是查看：全部退回队列，保持死信内容不变。
        foreach (var tag in deliveryTags)
            await channel.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken);
        return messages;
    }

    public async Task<int> ReplayAsync(int max, CancellationToken cancellationToken = default)
    {
        await using var channel = await OpenChannelAsync(cancellationToken);
        var replayed = 0;
        for (var index = 0; index < Math.Clamp(max, 1, 500); index++)
        {
            var result = await channel.BasicGetAsync(_options.DeadLetterQueue, autoAck: false, cancellationToken);
            if (result is null)
                break;

            var eventName = RabbitMQEventBus.ReadHeaderString(result.BasicProperties, RabbitMQEventBus.EventNameHeader)
                ?? result.RoutingKey;
            try
            {
                // 投递计数归零，让重放的消息重新获得完整的重试预算。
                var headers = new Dictionary<string, object?> { [RabbitMQEventBus.EventNameHeader] = eventName };
                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: eventName,
                    mandatory: true,
                    basicProperties: new BasicProperties { DeliveryMode = DeliveryModes.Persistent, Headers = headers },
                    body: result.Body.ToArray(),
                    cancellationToken: cancellationToken);
                await channel.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken);
                replayed++;
            }
            catch (Exception exception)
            {
                // 重投失败就退回死信队列，绝不丢。
                logger.LogError(exception, "Replaying dead letter '{EventName}' failed.", eventName);
                await channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                break;
            }
        }

        if (replayed > 0)
            logger.LogWarning("Replayed {Count} dead letters from {Queue}.", replayed, _options.DeadLetterQueue);
        return replayed;
    }

    private static DeadLetterMessage Describe(IReadOnlyBasicProperties properties, string routingKey, ReadOnlySpan<byte> body)
    {
        var payload = Encoding.UTF8.GetString(body);
        return new DeadLetterMessage(
            RabbitMQEventBus.ReadHeaderString(properties, RabbitMQEventBus.EventNameHeader) ?? routingKey,
            int.TryParse(RabbitMQEventBus.ReadHeaderString(properties, RabbitMQEventBus.AttemptHeader), out var attempts) ? attempts : 0,
            payload.Length > 2000 ? payload[..2000] : payload);
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        if (!connection.IsConnected)
            await connection.TryConnectAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken);
    }
}
