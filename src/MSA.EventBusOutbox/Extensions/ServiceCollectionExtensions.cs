using Microsoft.Extensions.DependencyInjection;


namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Outbox：TStore 通常为 EfOutboxStore&lt;TYourDbContext&gt;，
    /// 并启动后台轮询服务 OutboxProcessorService 负责真正发布到 EventBus。
    /// </summary>
    public static IServiceCollection AddOutbox<TStore>(
        this IServiceCollection services,
        Action<OutboxProcessorOptions>? configure = null)
        where TStore : class, IOutboxStore
    {
        services.AddScoped<IOutboxStore, TStore>();
        services.Configure<OutboxProcessorOptions>(o => configure?.Invoke(o));
        services.AddHostedService<OutboxProcessorService>();
        return services;
    }
}
