using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSA.EventBus.Abstractions;
using MSA.EventBusRabbitMQ;
using RabbitMQ.Client;

namespace MSA.EventBus.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>注册基于 RabbitMQ 的 EventBus（单例：内部维护长连接与共享消费Channel）</summary>
    public static IServiceCollection AddRabbitMQEventBus(this IServiceCollection services, Action<RabbitMQOptions> configure)
    {
        var options = new RabbitMQOptions();
        configure(options);
        services.AddSingleton(options);

        services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = true
        });

        services.AddSingleton<IRabbitMQPersistentConnection>(sp =>
        {
            var factory = sp.GetRequiredService<IConnectionFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DefaultRabbitMQPersistentConnection>>();
            return new DefaultRabbitMQPersistentConnection(factory, logger, options.RetryCount);
        });

        services.AddSingleton<RabbitMQTelemetry>();
        services.AddSingleton<IEventBus, RabbitMQEventBus>();
        services.AddSingleton<IHostedService>(sp => (RabbitMQEventBus)sp.GetRequiredService<IEventBus>());

        return services;
    }
}
