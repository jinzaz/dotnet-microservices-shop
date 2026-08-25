
using Microsoft.Extensions.DependencyInjection;

namespace MSA.Saga.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Saga 基础设施。默认使用内存仓储，生产环境请自行实现基于数据库的 ISagaRepository
    /// 并用 services.AddSingleton&lt;ISagaRepository, YourEfSagaRepository&gt;() 覆盖。
    /// </summary>
    public static IServiceCollection AddSaga(this IServiceCollection services)
    {
        services.AddSingleton<ISagaRepository, InMemorySagaRepository>();
        return services;
    }
}
