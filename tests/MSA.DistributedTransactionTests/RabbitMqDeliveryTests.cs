using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSA.EventBus.Abstractions;
using MSA.EventBus.DependencyInjection;
using RabbitMQ.Client;

namespace MSA.DistributedTransactionTests;

public sealed class RetryProbeIntegrationEvent : IntegrationEvent
{
    public string Marker { get; init; } = string.Empty;
}

/// <summary>Fails the first <see cref="FailuresBeforeSuccess"/> deliveries, then records the successful one.</summary>
public sealed class RetryProbeHandler : IIntegrationEventHandler<RetryProbeIntegrationEvent>
{
    private static int _failuresLeft;
    private static int _attempts;
    private static readonly Lock Gate = new();
    private static TaskCompletionSource _handled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int Attempts => Volatile.Read(ref _attempts);

    public static Task Handled => _handled.Task;

    public static void Arm(int failuresBeforeSuccess)
    {
        lock (Gate)
        {
            _failuresLeft = failuresBeforeSuccess;
            _attempts = 0;
            _handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public const int FailuresBeforeSuccess = 1;

    public Task HandleAsync(RetryProbeIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            _attempts++;
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException("downstream unavailable");
            }

            _handled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// 需要真实 RabbitMQ 的投递测试：重试队列回流与死信重放都是 broker 行为，只有连上 broker 才测得到。
/// 队列、交换机都带 test 前缀并在结束后删除，不会影响现有环境。没有配置 broker 时用例标记为 Inconclusive。
/// </summary>
[TestClass]
public sealed class RabbitMqDeliveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private string _queue = null!;
    private string _exchange = null!;
    private ServiceProvider? _provider;

    [TestInitialize]
    public void Initialize()
    {
        if (TestEnvironment.RabbitMq is null)
            Assert.Inconclusive("未配置 RabbitMQ（testsettings.json 或 MSA_TEST_RABBITMQ__HostName），跳过 broker 集成测试。");
        var run = Guid.NewGuid().ToString("N")[..8];
        _queue = $"{TestEnvironment.QueuePrefix}.dtx.{run}";
        _exchange = $"{TestEnvironment.QueuePrefix}.dtx.events.{run}";
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_provider is null)
            return;
        var hosted = _provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
            await service.StopAsync(CancellationToken.None);
        await DeleteTopologyAsync();
        await _provider.DisposeAsync();
        _provider = null;
    }

    /// <summary>消费失败的消息进入重试队列，TTL 到期后回到主队列并被成功处理，而不是一次失败就进死信。</summary>
    [TestMethod]
    public async Task AFailedDeliveryIsRetriedThroughTheRetryQueue()
    {
        RetryProbeHandler.Arm(RetryProbeHandler.FailuresBeforeSuccess);
        var provider = await StartBusAsync(maxDeliveryAttempts: 5, retryDelay: TimeSpan.FromSeconds(2));

        await PublishAsync(provider, "retry");

        await RetryProbeHandler.Handled.WaitAsync(Timeout);
        Assert.AreEqual(2, RetryProbeHandler.Attempts, "第一次失败，回流后第二次成功。");
        Assert.AreEqual(0u, await QueueDepthAsync($"{_queue}.dlq"), "重试成功的消息不应进入死信队列。");
    }

    /// <summary>重试次数用尽后消息进入死信队列，可以被查看，并在根因修复后重放回主流程。</summary>
    [TestMethod]
    public async Task AnExhaustedMessageLandsInTheDeadLetterQueueAndCanBeReplayed()
    {
        // 永远失败：两次投递之后进入死信队列。
        RetryProbeHandler.Arm(int.MaxValue);
        var provider = await StartBusAsync(maxDeliveryAttempts: 2, retryDelay: TimeSpan.FromSeconds(1));
        var admin = provider.GetRequiredService<IDeadLetterAdmin>();

        await PublishAsync(provider, "dead-letter");

        await WaitUntilAsync(async () => await QueueDepthAsync($"{_queue}.dlq") == 1, "消息应当进入死信队列");
        Assert.AreEqual(2, RetryProbeHandler.Attempts, "投递次数应当等于 MaxDeliveryAttempts。");

        var parked = await admin.PeekAsync(10);
        Assert.HasCount(1, parked);
        Assert.AreEqual(nameof(RetryProbeIntegrationEvent), parked[0].EventName, "死信里仍然认得出事件类型。");
        Assert.Contains("dead-letter", parked[0].Payload);
        Assert.AreEqual(1u, await QueueDepthAsync($"{_queue}.dlq"), "查看不应消费掉死信。");

        // 根因修复后重放。
        RetryProbeHandler.Arm(0);
        Assert.AreEqual(1, await admin.ReplayAsync(10));
        await RetryProbeHandler.Handled.WaitAsync(Timeout);
        await WaitUntilAsync(async () => await QueueDepthAsync($"{_queue}.dlq") == 0, "重放后的死信队列应当清空");
    }

    private async Task<ServiceProvider> StartBusAsync(int maxDeliveryAttempts, TimeSpan retryDelay)
    {
        var broker = TestEnvironment.RabbitMq!.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HostName"] = broker.HostName,
            ["Port"] = broker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["UserName"] = broker.UserName,
            ["Password"] = broker.Password,
            ["VirtualHost"] = broker.VirtualHost,
            ["QueueName"] = _queue,
            ["ExchangeName"] = _exchange,
            ["DeadLetterExchange"] = $"{_exchange}.dlx",
            ["MaxDeliveryAttempts"] = maxDeliveryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RetryDelay"] = retryDelay.ToString(),
            ["PrefetchCount"] = "1",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));
        services.AddRabbitMQEventBus(configuration);
        services.AddIntegrationEventHandler<RetryProbeIntegrationEvent, RetryProbeHandler>();
        _provider = services.BuildServiceProvider();

        foreach (var service in _provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
        // 消费者是在后台线程建立的，等队列真正出现再发消息。
        await WaitUntilAsync(async () => await QueueExistsAsync(_queue), "消费者队列应当被声明");
        return _provider;
    }

    /// <summary>
    /// 绑定是在消费者启动的最后一步建立的，AMQP 又没有「绑定是否存在」的查询。
    /// 发布用的是 mandatory，路由不到会抛异常，因此这里重试到绑定生效为止。
    /// </summary>
    private static async Task PublishAsync(ServiceProvider provider, string marker)
    {
        var bus = provider.GetRequiredService<IEventBus>();
        var started = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await bus.PublishAsync(new RetryProbeIntegrationEvent { Marker = marker });
                return;
            }
            catch (Exception) when (started.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(500);
            }
        }
    }

    private async Task<IConnection> ConnectAsync()
    {
        var broker = TestEnvironment.RabbitMq!.Value;
        var factory = new ConnectionFactory
        {
            HostName = broker.HostName,
            Port = broker.Port,
            UserName = broker.UserName,
            Password = broker.Password,
            VirtualHost = broker.VirtualHost,
        };
        return await factory.CreateConnectionAsync();
    }

    private async Task<uint> QueueDepthAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            var declared = await channel.QueueDeclarePassiveAsync(queue);
            return declared.MessageCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private async Task<bool> QueueExistsAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            await channel.QueueDeclarePassiveAsync(queue);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>测试用拓扑全部删除，broker 上不留痕迹。</summary>
    private async Task DeleteTopologyAsync()
    {
        try
        {
            await using var connection = await ConnectAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var queue in new[] { _queue, $"{_queue}.retry", $"{_queue}.dlq" })
                await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
            await channel.ExchangeDeleteAsync(_exchange);
            await channel.ExchangeDeleteAsync($"{_exchange}.dlx");
        }
        catch (Exception)
        {
            // 清理失败不应让测试结果变红。
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < Timeout)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }
        Assert.Fail($"等待超时：{because}");
    }
}
