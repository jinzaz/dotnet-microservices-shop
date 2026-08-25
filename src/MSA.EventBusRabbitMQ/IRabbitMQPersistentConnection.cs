using RabbitMQ.Client;

namespace MSA.EventBusRabbitMQ;


/// <summary>
/// 长连接封装：断线自动重连，供 EventBus 反复 CreateChannelAsync() 使用。
/// RabbitMQ.Client 7.x 起客户端 API 全面异步化（IConnection/IChannel 的所有操作
/// 均为 XxxAsync），因此本接口也相应改为异步方法，并实现 IAsyncDisposable。
/// </summary>
public interface IRabbitMQPersistentConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

    Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);
}
