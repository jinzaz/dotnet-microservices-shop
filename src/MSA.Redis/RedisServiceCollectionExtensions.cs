using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MSA.Redis;

/// <summary>
/// 键的管理分两层：本库只管「机制」—— 服务前缀、hash tag，以及挂在业务键后面的类型后缀
/// （<c>:gen</c> 代际、<c>:ns</c> 命名空间、<c>:lock</c> 锁）和失效广播频道 <c>cache:evict</c>；
/// 业务键由各服务在自己的 <c>XxxCacheKeys</c> 里按业务对象命名（<c>product:{id}</c>、<c>price:{sku}</c>），
/// 和失效规则放在一起。共享库不认识任何业务键，业务改键不需要动公共包。
/// 最终形如 <c>{catalog:product:42}</c>、<c>{catalog:product:42}:gen</c>、<c>{catalog:coupon-claim:7:u1:lock}</c>：
/// 先按服务、再按业务对象归类，类型只是最后一段，按业务排查与统计内存都只需一个前缀。
/// </summary>
public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddMsaRedis(
        this IServiceCollection services,
        IConfiguration configuration,
        string keyPrefix)
    {
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Redis is required.");

        services.AddSingleton(new RedisKeyspace(keyPrefix));
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            // Redis 暂时不可达时照常启动：缓存全部回源，连接在后台自动重试。
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddMemoryCache();
        services.AddSingleton<RedisCacheStore>();
        services.AddSingleton<ICacheStore>(provider => provider.GetRequiredService<RedisCacheStore>());
        services.AddSingleton<IChangeFeed, RedisChangeFeed>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IRedisMarkers, RedisMarkers>();
        services.AddHostedService<LocalCacheEvictionService>();
        return services;
    }

    /// <summary>声明「实体 → 缓存键」的失效规则，并登记提交后失效的拦截器。</summary>
    public static IServiceCollection AddMsaCacheInvalidation(
        this IServiceCollection services,
        Action<CacheInvalidationRules> configure)
    {
        var rules = new CacheInvalidationRules();
        configure(rules);
        services.AddSingleton(rules);
        services.AddSingleton<CacheInvalidationInterceptor>();
        return services;
    }

    /// <summary>在 <c>AddDbContext((provider, options) =&gt; ...)</c> 里调用，把提交后失效挂到这个上下文上。</summary>
    public static DbContextOptionsBuilder UseMsaCacheInvalidation(
        this DbContextOptionsBuilder options,
        IServiceProvider services) =>
        options.AddInterceptors(services.GetRequiredService<CacheInvalidationInterceptor>());
}

/// <summary>订阅失效广播，清掉本实例的一级缓存。广播丢了由一级缓存自身的短 TTL 兜底。</summary>
internal sealed class LocalCacheEvictionService(
    IChangeFeed changes,
    RedisCacheStore cache,
    ILogger<LocalCacheEvictionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var subscription = await changes.SubscribeAsync(
                    RedisCacheStore.EvictionTopic,
                    message => cache.EvictLocal(message.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                    stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Subscribing to cache eviction failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
