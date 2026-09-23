using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MSA.EventBus.Abstractions;
using MSA.EventBusRabbitMQ;
using RabbitMQ.Client;

namespace MSA.EventBus.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册基于 RabbitMQ 的 EventBus（单例：内部维护长连接与共享消费Channel）。
    /// <paramref name="configuration"/> 为 "RabbitMQ" 配置节，键名与 <see cref="RabbitMQOptions"/> 属性一致。
    /// </summary>
    public static IServiceCollection AddRabbitMQEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RabbitMQOptions>()
            .Bind(configuration)
            .Validate(options => !string.IsNullOrWhiteSpace(options.HostName), "RabbitMQ:HostName is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.QueueName),
                "RabbitMQ:QueueName is required and must be unique per service; a shared queue makes services steal each other's events.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionFactory>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = options.HostName,
                Port = options.Port,
                UserName = options.UserName,
                Password = options.Password,
                VirtualHost = options.VirtualHost,
                AutomaticRecoveryEnabled = true
            };
        });

        services.AddSingleton<IRabbitMQPersistentConnection>(sp =>
        {
            var factory = sp.GetRequiredService<IConnectionFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DefaultRabbitMQPersistentConnection>>();
            var retryCount = sp.GetRequiredService<IOptions<RabbitMQOptions>>().Value.RetryCount;
            return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
        });

        services.AddSingleton<RabbitMQTelemetry>();
        services.AddSingleton<IEventBus, RabbitMQEventBus>();
        services.AddSingleton<IHostedService>(sp => (RabbitMQEventBus)sp.GetRequiredService<IEventBus>());
        services.AddSingleton<IDeadLetterAdmin, RabbitMQDeadLetterAdmin>();

        return services;
    }
}
