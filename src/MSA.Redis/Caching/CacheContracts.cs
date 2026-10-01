namespace MSA.Redis;

/// <summary>
/// 一条缓存的读取策略。三个经典问题各有一个旋钮：
/// 穿透 —— 空结果也缓存（<see cref="NullExpiration"/>），不存在的键不会每次都打到数据库；
/// 击穿 —— 进程内同键只回源一次（固定开启）；
/// 雪崩 —— 过期时间加随机抖动（<see cref="Jitter"/>），同一批写入的键不会在同一秒一起失效。
/// </summary>
public sealed record CachePolicy(TimeSpan Expiration)
{
    /// <summary>空结果的缓存时长；设为 <see cref="TimeSpan.Zero"/> 表示不缓存空结果。</summary>
    public TimeSpan NullExpiration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>过期时间的随机上浮比例（0.1 = 最多多 10%）。</summary>
    public double Jitter { get; init; } = 0.1;

    /// <summary>
    /// 进程内一级缓存时长；null 表示只用 Redis。一级缓存靠 Pub/Sub 广播失效，
    /// 这个时长是广播丢失时的兜底上限，只给能容忍几秒旧值的读（商品详情、列表）打开。
    /// </summary>
    public TimeSpan? LocalExpiration { get; init; }
}

/// <summary>
/// 一次提交带来的缓存影响：要删的键、要整体作废的命名空间、要通知的变更主题。
/// 由失效规则在保存时收集，在事务提交之后才生效。
/// </summary>
public sealed class CacheChange
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _namespaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _topics = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _keys;
    public IReadOnlyCollection<string> Namespaces => _namespaces;
    public IReadOnlyCollection<string> Topics => _topics;
    public bool IsEmpty => _keys.Count == 0 && _namespaces.Count == 0 && _topics.Count == 0;

    public CacheChange Key(string key)
    {
        _keys.Add(key);
        return this;
    }

    /// <summary>列表、搜索这类「一个变更影响不定多个键」的缓存，用命名空间代际整体作废。</summary>
    public CacheChange Namespace(string name)
    {
        _namespaces.Add(name);
        return this;
    }

    /// <summary>提交后在变更流上通知这个主题（例如某个 SKU 的秒杀余量变了）。</summary>
    public CacheChange Topic(string topic)
    {
        _topics.Add(topic);
        return this;
    }
}

/// <summary>
/// Cache Aside 读写。回源结果写回缓存时带「代际」校验：读库期间只要发生过失效，这次写回就会被丢弃，
/// 从根上消除「读到旧值 → 写库并删缓存 → 旧值又被写回缓存」的并发脏数据。
/// Redis 不可用时一律直接回源，缓存永远只是加速，不是正确性的前提。
/// </summary>
public interface ICacheStore
{
    Task<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class;

    /// <summary>批量版：一次往返取多个键，只为没命中的键回源一次。返回里没有的键表示「不存在」。</summary>
    Task<IReadOnlyDictionary<string, T>> GetOrLoadManyAsync<T>(
        IReadOnlyCollection<string> keys,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, T>>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class;

    /// <summary>当前命名空间代际，拼进列表类缓存键里（形如 <c>products:v7</c>）。</summary>
    Task<string> NamespaceAsync(string name, CancellationToken cancellationToken);

    /// <summary>删除键并推进它们的代际、整体作废命名空间。主题不在这里处理，由变更流负责。</summary>
    Task InvalidateAsync(CacheChange change, CancellationToken cancellationToken = default);
}

/// <summary>不带缓存的实现：永远回源。测试与未配置 Redis 的场景用它，业务代码不需要分支。</summary>
public sealed class NullCacheStore : ICacheStore
{
    public static readonly NullCacheStore Instance = new();

    public Task<T?> GetOrLoadAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class => load(cancellationToken);

    public Task<IReadOnlyDictionary<string, T>> GetOrLoadManyAsync<T>(
        IReadOnlyCollection<string> keys,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, T>>> load,
        CachePolicy policy,
        CancellationToken cancellationToken)
        where T : class => load(keys, cancellationToken);

    public Task<string> NamespaceAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult($"{name}:v0");

    public Task InvalidateAsync(CacheChange change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
