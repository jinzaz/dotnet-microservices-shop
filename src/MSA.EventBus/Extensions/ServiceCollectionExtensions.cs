using Microsoft.Extensions.DependencyInjection;
using MSA.EventBus.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>注册一个集成事件处理器（供 DI 容器解析，配合 eventBus.Subscribe 使用）</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        // Use keyed services to register multiple handlers for the same event type
        // the consumer can use IKeyedServiceProvider.GetKeyedService<IIntegrationEventHandler>(typeof(T)) to get all
        // handlers for the event type.
        services.AddKeyedTransient<IIntegrationEventHandler, THandler>(typeof(TEvent));

        services.Configure<EventBusSubscriptionInfo>(o =>
        {
            // Keep track of all registered event types and their name mapping. We send these event types over the message bus
            // and we don't want to do Type.GetType, so we keep track of the name mapping here.

            // This list will also be used to subscribe to events from the underlying message broker implementation.
            o.EventTypes[typeof(TEvent).Name] = typeof(TEvent);
        });

        return services;
    }

    /// <summary>
    /// 扫描一个程序集里所有 IIntegrationEventHandler&lt;TEvent&gt; 的实现并逐一注册，
    /// 效果等同于对每一个手写一行 AddIntegrationEventHandler&lt;TEvent, THandler&gt;()。
    /// 反射只在启动时跑一次，不影响请求路径；跟 AOT/裁剪不兼容（本项目没启用）。
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandlersFromAssemblyContaining<TMarker>(this IServiceCollection services) =>
        services.AddIntegrationEventHandlersFromAssembly(typeof(TMarker).Assembly);

    [RequiresUnreferencedCode("反射扫描程序集里的处理器类型，和代码裁剪（trimming）/Native AOT 不兼容。")]
    public static IServiceCollection AddIntegrationEventHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var register = typeof(ServiceCollectionExtensions)
            .GetMethod(nameof(AddIntegrationEventHandler), BindingFlags.Public | BindingFlags.Static)!;

        foreach (var handlerType in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var @interface in handlerType.GetInterfaces())
            {
                if (!@interface.IsGenericType || @interface.GetGenericTypeDefinition() != typeof(IIntegrationEventHandler<>))
                    continue;

                var eventType = @interface.GetGenericArguments()[0];
                register.MakeGenericMethod(eventType, handlerType).Invoke(null, [services]);
            }
        }

        return services;
    }
}
