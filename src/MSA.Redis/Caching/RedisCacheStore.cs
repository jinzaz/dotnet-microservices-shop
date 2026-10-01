using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MSA.Redis;

internal sealed class RedisCacheStore(
    IConnectionMultiplexer redis,
    RedisKeyspace keyspace,
    IMemoryCache local,
    ILogger<RedisCacheStore> logger) : ICacheStore
{
    internal const string EvictionTopic = "cache:evict";

    // 空结果的占位：JSON 不会以 NUL 开头，不会和真实值混淆。
    private const string NullMarker = "\0";

    // 代际键保留一天：只要一次回源（读库到写回之间）不超过这个时长，失效就不会被漏判。
    private static readonly TimeSpan GenerationRetention = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // KEYS[1]=数据键 KEYS[2]=代际键；ARGV[1]=读库之前看到的代际（空串表示当时没有）ARGV[2]=值 ARGV[3]=毫秒 TTL。
    // 代际变了说明读库期间有人失效过，这份值可能是旧的，丢弃不写。
    private const string GuardedSetScript = """
        local current = redis.call('GET', KEYS[2])
        if (current == false and ARGV[1] == '') or current == ARGV[1] then
          redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
          return 1
        end
        return 0
        """;

    // KEYS[1]=数据键 KEYS[2]=代际键；ARGV[1]=代际键保留毫秒。删除与推进代际必须原子，否则中间仍可能被写回。
    private const string InvalidateScript = """
        redis.call('DEL', KEYS[1])
        redis.call('INCR', KEYS[2])
        redis.call('PEXPIRE', KEYS[2], ARGV[1])
        return 1
        """;

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _flights = new(StringComparer.Ordinal);

    public async Task<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class
    {
        var dataKey = keyspace.Key(key);
        if (policy.LocalExpiration is not null && local.TryGetValue(LocalKey(dataKey), out string? localHit))
            return Decode<T>(localHit!);

        RedisValue[] values;
        try
        {
            values = await Database.StringGetAsync([dataKey, GenerationKey(dataKey)]).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(exception, "Redis read failed for {CacheKey}; loading from the source.", key);
            return await load(cancellationToken);
        }

        if (values[0].HasValue)
        {
            var cached = (string)values[0]!;
            RememberLocally(dataKey, cached, policy);
            return Decode<T>(cached);
        }

        var observedGeneration = values[1].HasValue ? (string)values[1]! : string.Empty;
        string payload;
        try
        {
            payload = await SingleFlightAsync(dataKey, observedGeneration, load, policy, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                                          && exception is OperationCanceledException or ObjectDisposedException)
        {
            // 领头的那次回源随它自己的请求被取消了；跟随者不背这个锅，自己再读一次。
            return await load(cancellationToken);
        }

        RememberLocally(dataKey, payload, policy);
        return Decode<T>(payload);
    }

    public async Task<IReadOnlyDictionary<string, T>> GetOrLoadManyAsync<T>(
        IReadOnlyCollection<string> keys,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, T>>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        if (keys.Count == 0)
            return result;

        var distinct = keys.Distinct(StringComparer.Ordinal).ToArray();
        (string Key, RedisValue Value, RedisValue Generation)[] lookups;
        try
        {
            // 每个键单独 GET（自动流水线），不用跨槽的 MGET，集群模式下同样成立。
            var db = Database;
            var pending = distinct
                .Select(key => (Key: key, Data: keyspace.Key(key)))
                .Select(item => (item.Key,
                    Value: db.StringGetAsync(item.Data),
                    Generation: db.StringGetAsync(GenerationKey(item.Data))))
                .ToArray();
            await Task.WhenAll(pending.SelectMany(item => new[] { item.Value, item.Generation })).WaitAsync(cancellationToken);
            lookups = pending.Select(item => (item.Key, item.Value.Result, item.Generation.Result)).ToArray();
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(exception, "Redis batch read failed for {Count} keys; loading from the source.", distinct.Length);
            return await load(distinct, cancellationToken);
        }

        var misses = new List<(string Key, string Generation)>();
        foreach (var (key, value, generation) in lookups)
        {
            if (!value.HasValue)
            {
                misses.Add((key, generation.HasValue ? (string)generation! : string.Empty));
                continue;
            }
            if (Decode<T>((string)value!) is { } hit)
                result[key] = hit;
        }
        if (misses.Count == 0)
            return result;

        // ponytail: 批量回源不做进程内合并，同一批冷键被并发请求时会各查一次；热点单键请用 GetOrLoadAsync。
        var loaded = await load(misses.Select(miss => miss.Key).ToArray(), cancellationToken);
        var writes = new List<Task>(misses.Count);
        foreach (var (key, generation) in misses)
        {
            loaded.TryGetValue(key, out var value);
            if (value is not null)
                result[key] = value;
            writes.Add(TryWriteBackAsync(keyspace.Key(key), generation, Encode(value), policy, value is null));
        }
        await Task.WhenAll(writes);
        return result;
    }

    public async Task<string> NamespaceAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var version = await Database.StringGetAsync(NamespaceKey(name)).WaitAsync(cancellationToken);
            return $"{name}:v{(version.HasValue ? (string)version! : "0")}";
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(exception, "Redis namespace read failed for {Namespace}.", name);
            // 读不到代际就给一个一次性的版本，保证不会命中可能过期的旧列表。
            return $"{name}:x{Guid.NewGuid():N}";
        }
    }

    public async Task InvalidateAsync(CacheChange change, CancellationToken cancellationToken = default)
    {
        if (change.Keys.Count == 0 && change.Namespaces.Count == 0)
            return;

        var db = Database;
        var retention = (long)GenerationRetention.TotalMilliseconds;
        var work = change.Keys
            .Select(key => keyspace.Key(key))
            .Select(dataKey => (Task)db.ScriptEvaluateAsync(
                InvalidateScript,
                [dataKey, GenerationKey(dataKey)],
                [retention]))
            .Concat(change.Namespaces.Select(name => (Task)db.StringIncrementAsync(NamespaceKey(name))))
            .ToArray();

        // 本进程的一级缓存立刻清掉，其他实例靠广播。
        EvictLocal(change.Keys);
        await Task.WhenAll(work).WaitAsync(cancellationToken);
        if (change.Keys.Count > 0)
            await redis.GetSubscriber().PublishAsync(keyspace.Channel(EvictionTopic), string.Join('\n', change.Keys));
    }

    internal void EvictLocal(IEnumerable<string> logicalKeys)
    {
        foreach (var key in logicalKeys)
            local.Remove(LocalKey(keyspace.Key(key)));
    }

    private IDatabase Database => redis.GetDatabase();

    private async Task<string> SingleFlightAsync<T>(
        RedisKey dataKey,
        string observedGeneration,
        Func<CancellationToken, Task<T?>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class
    {
        var flightKey = (string)dataKey!;
        var flight = _flights.GetOrAdd(flightKey, flightId => new Lazy<Task<string>>(async () =>
        {
            try
            {
                var value = await load(cancellationToken);
                var payload = Encode(value);
                await TryWriteBackAsync(dataKey, observedGeneration, payload, policy, value is null);
                return payload;
            }
            finally
            {
                _flights.TryRemove(flightKey, out _);
            }
        }));
        return await flight.Value.WaitAsync(cancellationToken);
    }

    private async Task TryWriteBackAsync(
        RedisKey dataKey,
        string observedGeneration,
        string payload,
        CachePolicy policy,
        bool isNull)
    {
        var expiration = isNull ? policy.NullExpiration : policy.Expiration;
        if (expiration <= TimeSpan.Zero)
            return;

        try
        {
            await Database.ScriptEvaluateAsync(
                GuardedSetScript,
                [dataKey, GenerationKey(dataKey)],
                [observedGeneration, payload, (long)WithJitter(expiration, policy.Jitter).TotalMilliseconds]);
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            logger.LogWarning(exception, "Redis write-back failed for {CacheKey}.", (string)dataKey!);
        }
    }

    private void RememberLocally(RedisKey dataKey, string payload, CachePolicy policy)
    {
        if (policy.LocalExpiration is { } expiration && expiration > TimeSpan.Zero)
            local.Set(LocalKey(dataKey), payload, expiration);
    }

    private static TimeSpan WithJitter(TimeSpan expiration, double jitter) =>
        jitter <= 0
            ? expiration
            : expiration + TimeSpan.FromMilliseconds(expiration.TotalMilliseconds * jitter * Random.Shared.NextDouble());

    private static string Encode<T>(T? value) where T : class =>
        value is null ? NullMarker : JsonSerializer.Serialize(value, Json);

    private static T? Decode<T>(string payload) where T : class =>
        payload == NullMarker ? null : JsonSerializer.Deserialize<T>(payload, Json);

    private static RedisKey GenerationKey(RedisKey dataKey) => dataKey.Append(":gen");

    private RedisKey NamespaceKey(string name) => keyspace.Key($"{name}:ns");

    private static string LocalKey(RedisKey dataKey) => $"msa-cache:{dataKey}";

    private static bool IsRedisFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;
}
