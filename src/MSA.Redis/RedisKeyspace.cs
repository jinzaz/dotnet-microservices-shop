using StackExchange.Redis;

namespace MSA.Redis;

/// <summary>
/// 每个服务一段独立的键前缀。各服务虽然用不同的逻辑库，但 Pub/Sub 频道是跨库全局的，
/// 所以频道名也要带前缀，否则两个服务的同名主题会互相串台。
/// </summary>
public sealed class RedisKeyspace
{
    public RedisKeyspace(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.Contains('{') || prefix.Contains('}'))
            throw new ArgumentException("The key prefix cannot contain hash-tag braces.", nameof(prefix));
        Prefix = prefix.Trim();
    }

    public string Prefix { get; }

    /// <summary>
    /// 业务键用 hash tag 包起来，数据键与它的代际键落在同一个槽里，集群模式下 Lua 脚本才能同时操作两者。
    /// </summary>
    public RedisKey Key(string logicalKey) => $"{{{Prefix}:{logicalKey}}}";

    public RedisChannel Channel(string topic) => RedisChannel.Literal($"{Prefix}:{topic}");
}
