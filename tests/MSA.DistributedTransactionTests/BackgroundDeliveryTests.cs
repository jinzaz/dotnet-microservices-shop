using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.EventBusOutbox.Extensions;
using MSA.Repositories.Persistence;
using MSA.Saga;
using MSA.Saga.Extensions;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：真实的后台投递与超时服务（OutboxProcessorService / SagaTimeoutService）在失败与重试下的行为。
/// 这两个服务按真实时间轮询，因此用很短的轮询间隔加超时等待来断言，而不是固定 sleep。
/// </summary>
[TestClass]
public sealed class BackgroundDeliveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>已提交的 Outbox 消息最终会被后台服务发布并标记为 Published。</summary>
    [TestMethod]
    public async Task OutboxProcessorEventuallyPublishesCommittedMessages()
    {
        var bus = new FakeEventBus();
        await AddPendingAsync("acct-1");
        await using var provider = BuildProvider(bus);

        await RunHostedServicesAsync(provider, async () =>
        {
            await WaitUntilAsync(() => bus.Published.Count == 1, "消息应当被发布");
            await WaitUntilAsync(async () =>
            {
                await using var verify = NewContext();
                return await verify.OutboxMessages.AllAsync(message => message.Status == OutboxStatus.Published);
            }, "发布后应当标记为 Published");
        });
    }

    /// <summary>发布瞬时失败后会自动重试，不需要人工介入，也不会丢消息。</summary>
    [TestMethod]
    public async Task OutboxProcessorRetriesATransientPublishFailure()
    {
        var bus = new FakeEventBus();
        bus.FailNextPublishes(1);
        await AddPendingAsync("acct-2");
        await using var provider = BuildProvider(bus);

        await RunHostedServicesAsync(provider, async () =>
        {
            await WaitUntilAsync(() => bus.Published.Count == 1, "重试之后消息仍然会被发布");
            // 发布与标记是两步，等标记落库再断言，否则会读到 Processing。
            await WaitUntilAsync(async () =>
            {
                await using var pending = NewContext();
                return await pending.OutboxMessages.AllAsync(message => message.Status == OutboxStatus.Published);
            }, "发布后应当标记为 Published");

            await using var verify = NewContext();
            var message = await verify.OutboxMessages.SingleAsync();
            Assert.AreEqual(1, message.RetryCount, "失败一次后应当记录重试次数。");
        });
    }

    /// <summary>取满一批说明还有积压，发布器立刻取下一批，不等下一个轮询周期。</summary>
    [TestMethod]
    public async Task OutboxProcessorDrainsABacklogWithoutWaitingForTheNextTick()
    {
        var bus = new FakeEventBus();
        for (var i = 0; i < 5; i++)
            await AddPendingAsync($"acct-backlog-{i}");
        // 轮询间隔远大于等待超时：只有连续取批才能在超时内发完。
        await using var provider = BuildProvider(bus, configureOutbox: options =>
        {
            options.BatchSize = 2;
            options.PollingInterval = TimeSpan.FromHours(1);
        });

        await RunHostedServicesAsync(provider, () =>
            WaitUntilAsync(() => bus.Published.Count == 5, "积压的消息应当在同一轮里连续发完"));
    }

    /// <summary>到期的 Saga 会被超时服务接管处理，处理完成后超时被清除，不会反复触发。</summary>
    [TestMethod]
    public async Task SagaTimeoutServiceRunsTheHandlerAndClearsTheTimeout()
    {
        var correlationId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            ISagaStore store = db;
            var saga = SagaState.Start(correlationId, RecordingTimeoutHandler.Type, "AwaitingPayment", "{}", DateTimeOffset.UtcNow.AddMinutes(-10));
            store.Add(saga);
            saga.ScheduleTimeout(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-10));
            await store.SaveChangesAsync();
        }

        var handler = new RecordingTimeoutHandler();
        await using var provider = BuildProvider(new FakeEventBus(), handler);

        await RunHostedServicesAsync(provider, async () =>
        {
            await WaitUntilAsync(() => handler.Calls >= 1, "到期的 Saga 应当被处理");
            await WaitUntilAsync(async () =>
            {
                await using var verify = NewContext();
                var saga = await ((ISagaStore)verify).FindAsync(correlationId);
                return saga is { Status: SagaStatus.Compensated, TimeoutOnUtc: null };
            }, "补偿完成后超时应当被清除");

            var callsAfterCompletion = handler.Calls;
            await Task.Delay(300);
            Assert.AreEqual(callsAfterCompletion, handler.Calls, "已处理的 Saga 不应被反复触发。");
        });
    }

    /// <summary>
    /// 抢占消息时数据库抛异常（例如数据库重启）不能让发布器退出：BackgroundService 的未处理异常默认会停掉整个进程。
    /// </summary>
    [TestMethod]
    public async Task OutboxProcessorSurvivesATransientStoreFailure()
    {
        var bus = new FakeEventBus();
        await AddPendingAsync("acct-3");
        FlakyOutboxStore.Arm(2);
        await using var provider = BuildProvider(bus, configure: services =>
            services.AddScoped<IOutboxStore>(sp => new FlakyOutboxStore(sp.GetRequiredService<LedgerDbContext>())));

        await RunHostedServicesAsync(provider, async () =>
        {
            await WaitUntilAsync(() => FlakyOutboxStore.Failures >= 2, "抢占应当先失败几次");
            await WaitUntilAsync(() => bus.Published.Count == 1, "数据库恢复后发布器必须还活着并继续发布");
        });
    }

    /// <summary>Throws on the first few claims, then delegates to the real store.</summary>
    private sealed class FlakyOutboxStore(LedgerDbContext inner) : IOutboxStore
    {
        // 计数是静态的：后台服务每一轮都会新建作用域，实例级计数器会在每轮被重置。
        private static int _remaining;
        private static int _failures;

        public static int Failures => Volatile.Read(ref _failures);

        public static void Arm(int failures)
        {
            Interlocked.Exchange(ref _remaining, failures);
            Interlocked.Exchange(ref _failures, 0);
        }

        public void Add(OutboxMessage message) => ((IOutboxStore)inner).Add(message);

        public Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
            Guid lockId, int batchSize, int maxRetries, DateTimeOffset now, TimeSpan lockDuration, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) >= 0)
            {
                Interlocked.Increment(ref _failures);
                throw new InvalidOperationException("connection reset by peer");
            }
            return ((IOutboxStore)inner).ClaimPendingAsync(lockId, batchSize, maxRetries, now, lockDuration, cancellationToken);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => ((IOutboxStore)inner).SaveChangesAsync(cancellationToken);
    }

    private async Task AddPendingAsync(string account)
    {
        await using var db = NewContext();
        IOutboxStore outbox = db;
        await using var transaction = await db.BeginTransactionAsync();
        db.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), account, 1m));
        outbox.AddIntegrationEvent(new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = account, Amount = 1m });
        await db.CommitTransactionAsync(transaction);
    }

    private LedgerDbContext NewContext() => _harness.NewContext();

    private ServiceProvider BuildProvider(
        FakeEventBus bus,
        ISagaTimeoutHandler? timeoutHandler = null,
        Action<ServiceCollection>? configure = null,
        Action<OutboxProcessorOptions>? configureOutbox = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IMediator>(Mock.Of<IMediator>());
        services.AddSingleton<IEventBus>(bus);
        _harness.AddContext(services);
        services.AddOutbox<LedgerDbContext>(options =>
        {
            options.PollingInterval = TimeSpan.FromMilliseconds(50);
            options.LockDuration = TimeSpan.FromSeconds(5);
            configureOutbox?.Invoke(options);
        });
        services.AddSaga<LedgerDbContext>(options =>
        {
            options.PollingInterval = TimeSpan.FromMilliseconds(50);
            options.LockDuration = TimeSpan.FromSeconds(5);
        });
        if (timeoutHandler is not null)
            services.AddSingleton(timeoutHandler);
        // 最后注册覆盖 AddOutbox 的默认实现。
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task RunHostedServicesAsync(ServiceProvider provider, Func<Task> assertions)
    {
        var hosted = provider.GetServices<IHostedService>().ToArray();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);
        try
        {
            await assertions();
        }
        finally
        {
            foreach (var service in hosted)
                await service.StopAsync(CancellationToken.None);
        }
    }

    private static Task WaitUntilAsync(Func<bool> condition, string because) =>
        WaitUntilAsync(() => Task.FromResult(condition()), because);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < Timeout)
        {
            if (await condition())
                return;
            await Task.Delay(25);
        }
        Assert.Fail($"等待超时：{because}");
    }

    private sealed class RecordingTimeoutHandler : ISagaTimeoutHandler
    {
        public const string Type = "LedgerTimeout";

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string SagaType => Type;

        public Task HandleTimeoutAsync(SagaState saga, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            saga.BeginCompensation("Compensating", "{}", DateTimeOffset.UtcNow);
            saga.Compensate(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }
}
