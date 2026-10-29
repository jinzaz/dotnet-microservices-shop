using Microsoft.Extensions.DependencyInjection;
namespace MSA.EventBusOutbox.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox<TStore>(this IServiceCollection services, Action<OutboxProcessorOptions>? configure = null) where TStore : class, IOutboxStore
    {
        services.AddScoped<IOutboxStore>(sp => sp.GetRequiredService<TStore>());
        services.TryAddTimeProvider();
        // 配置节 Outbox（环境变量 Outbox__PublishConcurrency 等）先绑定，代码里的委托再覆盖。
        services.AddOptions<OutboxProcessorOptions>().BindConfiguration(OutboxProcessorOptions.SectionName).Configure(options => configure?.Invoke(options));
        services.AddHostedService<OutboxProcessorService>(); return services;
    }
    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(x => x.ServiceType == typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
    }
}
