using Microsoft.Extensions.DependencyInjection;
namespace MSA.Saga.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSaga<TStore>(this IServiceCollection services, Action<SagaTimeoutOptions>? configure = null) where TStore : class, ISagaStore
    {
        services.AddScoped<ISagaStore>(sp => sp.GetRequiredService<TStore>());
        if (!services.Any(x => x.ServiceType == typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
        services.AddOptions<SagaTimeoutOptions>().Configure(options => configure?.Invoke(options));
        services.AddHostedService<SagaTimeoutService>(); return services;
    }
}
