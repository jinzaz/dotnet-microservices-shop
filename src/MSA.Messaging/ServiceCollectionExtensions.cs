using Microsoft.Extensions.DependencyInjection;
using System;

namespace MSA.Libraries.Messaging
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMessagingWithSaga(this IServiceCollection services)
        {
            // Register repository implementations if not already registered by the consumer
            services.AddScoped<IOutboxRepository, EfOutboxRepository>();
            services.AddScoped<ISagaRepository, EfSagaRepository>();
            // Event bus: default to in-memory for local/dev. Replace with RabbitMQ implementation in production.
            services.AddSingleton<IEventBus>(sp => new InMemoryEventBus());

            // Saga coordinator as singleton so it can subscribe and route events
            services.AddSingleton<SagaCoordinator>();

            // Register handlers automatically if present
            services.AddSingleton<SagaRegistrationHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<SagaRegistrationHostedService>());

            return services;
        }

        public static IServiceCollection AddMessagingWithSaga(this IServiceCollection services, Action<RabbitMqOptions> configure)
        {
            var options = new RabbitMqOptions();
            configure?.Invoke(options);

            services.AddScoped<IOutboxRepository, EfOutboxRepository>();
            services.AddScoped<ISagaRepository, EfSagaRepository>();

            // Register RabbitMQ-backed bus
            services.AddSingleton<IEventBus>(sp => new RabbitMqEventBus(options.HostName, options.Exchange, options.Port));
            services.AddSingleton<SagaCoordinator>();

            services.AddSingleton<SagaRegistrationHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<SagaRegistrationHostedService>());

            return services;
        }
    }

    public class RabbitMqOptions
    {
        public string HostName { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string Exchange { get; set; } = "events";
    }
}
