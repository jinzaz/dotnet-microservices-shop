using Grpc.Core;
using MediatR;
using Moq;
using MSA.Redis;

namespace MSA.CommerceTests;

/// <summary>
/// 进程内对接 gRPC 的最小装置：生成的客户端桩直接调对端的 gRPC 服务实现，
/// 两侧的契约映射都被真正走一遍，而不需要起端口。
/// </summary>
internal static class InProcessGrpc
{
    public static ISender Sender<TRequest, TResponse>(IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        var sender = new Mock<ISender>();
        sender
            .Setup(value => value.Send(It.IsAny<TRequest>(), It.IsAny<CancellationToken>()))
            .Returns((TRequest request, CancellationToken cancellationToken) => handler.Handle(request, cancellationToken));
        return sender.Object;
    }

    public static AsyncUnaryCall<TResponse> Call<TResponse>(Task<TResponse> response) =>
        new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    public static ServerCallContext Context(CancellationToken cancellationToken) => new FlowCallContext(cancellationToken);

    /// <summary>服务端只用到 CancellationToken，其余成员给出最小可用实现即可。</summary>
    private sealed class FlowCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        protected override string MethodCore => "flow";
        protected override string HostCore => "in-process";
        protected override string PeerCore => "in-process";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(30);
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => cancellationToken;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; } = Status.DefaultSuccess;
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}

/// <summary>进程内的变更流：和 Redis Pub/Sub 一样「至多一次、只提醒不带数据」，但同步投递，测试不需要等。</summary>
internal sealed class InMemoryChangeFeed : IChangeFeed
{
    private readonly Lock _gate = new();
    private readonly List<(string Topic, Action<string> Handler)> _subscribers = [];

    public Task PublishAsync(string topic, string message = "", CancellationToken cancellationToken = default)
    {
        Action<string>[] handlers;
        lock (_gate)
            handlers = _subscribers.Where(item => item.Topic == topic).Select(item => item.Handler).ToArray();
        foreach (var handler in handlers)
            handler(message);
        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Action<string> onMessage,
        CancellationToken cancellationToken = default)
    {
        var entry = (topic, onMessage);
        lock (_gate)
            _subscribers.Add(entry);
        return Task.FromResult<IAsyncDisposable>(new Subscription(() =>
        {
            lock (_gate)
                _subscribers.Remove(entry);
        }));
    }

    private sealed class Subscription(Action remove) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            remove();
            return ValueTask.CompletedTask;
        }
    }
}
