using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;

namespace MSA.CommerceTests;

[TestClass]
public sealed class EventBusRegistrationTests
{
    [TestMethod]
    public void AddRabbitMQEventBus_BindsSectionIntoTheOptionsTheBusReads()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["RabbitMQ:HostName"] = "rabbit.local",
            ["RabbitMQ:VirtualHost"] = "/app",
            ["RabbitMQ:QueueName"] = "msa.basket",
            ["RabbitMQ:PrefetchCount"] = "32",
        });

        // RabbitMQEventBus consumes IOptions<RabbitMQOptions>; the queue must come from configuration,
        // otherwise services share one queue and steal each other's events.
        var options = provider.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
        Assert.AreEqual("msa.basket", options.QueueName);
        Assert.AreEqual("msa.basket.dlq", options.DeadLetterQueue, "The dead-letter queue follows the service queue by default.");
        Assert.AreEqual((ushort)32, options.PrefetchCount);
        var factory = provider.GetRequiredService<RabbitMQ.Client.IConnectionFactory>();
        Assert.AreEqual("/app", factory.VirtualHost);
    }

    [TestMethod]
    public void AddRabbitMQEventBus_MissingQueueName_FailsValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["RabbitMQ:HostName"] = "rabbit.local" });

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RabbitMQOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMQEventBus(configuration.GetSection(RabbitMQOptions.SectionName));
        return services.BuildServiceProvider();
    }
}
