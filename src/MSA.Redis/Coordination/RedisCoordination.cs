using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MSA.Redis;

/// <summary>
/// 轻量变更通知：发布「某个主题变了」，订阅方自己去读最新状态。
/// Pub/Sub 是至多一次投递，所以它只能用来「提醒」，订阅方必须有定时刷新兜底，不能靠它传递数据本身。
/// </summary>
public interface IChangeFeed
{
    Task PublishAsync(string topic, string message = "", CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Action<string> onMessage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 分布式互斥：SET NX PX 加随机令牌，释放时比对令牌再删，不会误删别人续上的锁。
/// 没有看门狗续期 —— 租期要大于临界区的最长耗时，拿不到锁返回 null 由调用方决定是等还是放弃。
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan lease,
        CancellationToken cancellationToken = default);
}

/// <summary>带过期时间的布尔标记（例如「这场活动已抢光」），给热点路径做一次 O(1) 的前置拦截。</summary>
public interface IRedisMarkers
{
    Task SetAsync(string name, TimeSpan expiration, CancellationToken cancellationToken = default);
    Task<bool> IsSetAsync(string name, CancellationToken cancellationToken = default);
    Task ClearAsync(string name, CancellationToken cancellationToken = default);
}

internal sealed class RedisChangeFeed(IConnectionMultiplexer redis, RedisKeyspace keyspace) : IChangeFeed
{
    public Task PublishAsync(string topic, string message = "", CancellationToken cancellationToken = default) =>
        redis.GetSubscriber().PublishAsync(keyspace.Channel(topic), message).WaitAsync(cancellationToken);

    public async Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        Action<string> onMessage,
        CancellationToken cancellationToken = default)
    {
        // 每个订阅一条独立队列：同一频道上的多个订阅者互不影响，退订也只摘掉自己这一条。
        var queue = await redis.GetSubscriber().SubscribeAsync(keyspace.Channel(topic)).WaitAsync(cancellationToken);
        queue.OnMessage(message => onMessage(message.Message.HasValue ? (string)message.Message! : string.Empty));
        return new Subscription(queue);
    }

    private sealed class Subscription(ChannelMessageQueue queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await queue.UnsubscribeAsync();
            }
            catch (RedisException)
            {
                // 连接已经断了，订阅自然也不在了。
            }
        }
    }
}

internal sealed class RedisDistributedLock(
    IConnectionMultiplexer redis,
    RedisKeyspace keyspace,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
          return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        var key = keyspace.Key($"{resource}:lock");
        var token = Guid.NewGuid().ToString("N");
        var acquired = await redis.GetDatabase()
            .StringSetAsync(key, token, lease, When.NotExists)
            .WaitAsync(cancellationToken);
        return acquired ? new Lease(redis, key, token, logger) : null;
    }

    private sealed class Lease(IConnectionMultiplexer redis, RedisKey key, string token, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [key], [token]);
            }
            catch (RedisException exception)
            {
                // 释放失败不影响正确性：租期到了锁自己会过期。
                logger.LogWarning(exception, "Releasing lock {LockKey} failed; it will expire on its own.", (string)key!);
            }
        }
    }
}

internal sealed class RedisMarkers(IConnectionMultiplexer redis, RedisKeyspace keyspace) : IRedisMarkers
{
    public Task SetAsync(string name, TimeSpan expiration, CancellationToken cancellationToken = default) =>
        redis.GetDatabase().StringSetAsync(Key(name), "1", expiration).WaitAsync(cancellationToken);

    public Task<bool> IsSetAsync(string name, CancellationToken cancellationToken = default) =>
        redis.GetDatabase().KeyExistsAsync(Key(name)).WaitAsync(cancellationToken);

    public Task ClearAsync(string name, CancellationToken cancellationToken = default) =>
        redis.GetDatabase().KeyDeleteAsync(Key(name)).WaitAsync(cancellationToken);

    // 标记名本身就是业务键（例如 coupon-campaign:{id}:sold-out），因此也能被提交后失效规则直接清掉。
    private RedisKey Key(string name) => keyspace.Key(name);
}
