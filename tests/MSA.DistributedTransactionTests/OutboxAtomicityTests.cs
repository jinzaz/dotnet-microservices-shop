using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：本地事务与消息发布的原子性（dual write）、崩溃后补发、竞争消费者租约与重试退避。
/// </summary>
[TestClass]
public sealed class OutboxAtomicityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>
    /// 双写问题：直接调用消息总线的事件在业务回滚后依然被别人看见；走 Outbox 的事件和业务数据一起回滚。
    /// </summary>
    [TestMethod]
    public async Task DualWritePublishesAGhostEvent_WhileTheOutboxRollsBackWithTheBusiness()
    {
        var bus = new FakeEventBus();
        await using var db = _harness.NewContext();
        IOutboxStore outbox = db;

        await using (var transaction = await db.BeginTransactionAsync())
        {
            db.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), "acct-1", 100m));
            outbox.AddIntegrationEvent(new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = "acct-1", Amount = 100m });

            // 反模式：在同一个业务事务里直接发布。消息已经离开进程，事务却还没提交。
            await bus.PublishAsync(new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = "acct-1", Amount = 100m });

            await db.RollbackTransactionAsync();
        }

        await using var verify = _harness.NewContext();
        Assert.IsFalse(await verify.LedgerEntries.AnyAsync(), "业务数据已回滚。");
        Assert.IsFalse(await verify.OutboxMessages.AnyAsync(), "Outbox 消息与业务数据同生共死。");
        Assert.HasCount(1, bus.Published, "直接发布的事件无法回滚，下游会收到一条并不存在的业务变更。");
    }

    /// <summary>进程在发布前崩溃：消息已随业务事务落库，重启后仍会被取走发布（最终一致）。</summary>
    [TestMethod]
    public async Task CommittedOutboxMessageSurvivesACrashBeforePublishing()
    {
        await using (var db = _harness.NewContext())
        {
            IOutboxStore outbox = db;
            await using var transaction = await db.BeginTransactionAsync();
            db.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), "acct-2", 30m));
            outbox.AddIntegrationEvent(new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = "acct-2", Amount = 30m });
            await db.CommitTransactionAsync(transaction);
        }

        // “重启”后的新进程
        await using var restarted = _harness.NewContext();
        IOutboxStore store = restarted;
        // AddIntegrationEvent 用真实时钟登记 NextAttemptOnUtc，所以这里也按真实时钟取用。
        var claimed = await store.ClaimPendingAsync(
            Guid.NewGuid(), 10, 5, DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.HasCount(1, claimed);
        Assert.AreEqual(OutboxStatus.Processing, claimed[0].Status);
    }

    /// <summary>竞争消费者：租约内消息对其他实例不可见；租约过期后可以被别的实例接管，避免消息卡死。</summary>
    [TestMethod]
    public async Task LeaseHidesTheMessageFromACompetingWorkerUntilItExpires()
    {
        await AddPendingAsync("acct-3");
        var lease = TimeSpan.FromMinutes(1);

        await using var workerA = _harness.NewContext();
        await using var workerB = _harness.NewContext();
        var claimedByA = await ((IOutboxStore)workerA).ClaimPendingAsync(Guid.NewGuid(), 10, 5, Now, lease, CancellationToken.None);
        var claimedByB = await ((IOutboxStore)workerB).ClaimPendingAsync(Guid.NewGuid(), 10, 5, Now, lease, CancellationToken.None);

        Assert.HasCount(1, claimedByA);
        Assert.IsEmpty(claimedByB, "同一条消息不能被两个实例同时发布。");

        // A 崩溃了，既没有发布也没有释放租约。
        var claimedAfterExpiry = await ((IOutboxStore)workerB).ClaimPendingAsync(
            Guid.NewGuid(), 10, 5, Now.Add(lease).AddSeconds(1), lease, CancellationToken.None);
        Assert.HasCount(1, claimedAfterExpiry, "租约过期后消息必须能被接管，否则会永久卡住。");
    }

    /// <summary>发布持续失败：指数退避期间不重复取，达到上限后置为 Failed 并停止重试（需要人工/告警介入）。</summary>
    [TestMethod]
    public async Task RepeatedPublishFailuresBackOffAndEndAsFailed()
    {
        await AddPendingAsync("acct-4");
        const int maxRetries = 3;
        var now = Now;

        await using var db = _harness.NewContext();
        IOutboxStore store = db;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var lockId = Guid.NewGuid();
            var batch = await store.ClaimPendingAsync(lockId, 10, maxRetries, now, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.HasCount(1, batch, $"第 {attempt} 次重试应该能取到消息。");

            batch[0].MarkFailed(lockId, new InvalidOperationException("broker down"), now.AddSeconds(Math.Pow(2, attempt)), maxRetries);
            await store.SaveChangesAsync(CancellationToken.None);

            var tooEarly = await store.ClaimPendingAsync(Guid.NewGuid(), 10, maxRetries, now, TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.IsEmpty(tooEarly, "退避时间未到时不应重复取用。");
            now = now.AddSeconds(Math.Pow(2, attempt) + 1);
        }

        var message = await db.OutboxMessages.SingleAsync();
        Assert.AreEqual(OutboxStatus.Failed, message.Status);
        Assert.AreEqual(maxRetries, message.RetryCount);
        var afterFailure = await store.ClaimPendingAsync(Guid.NewGuid(), 10, maxRetries, now.AddHours(1), TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.IsEmpty(afterFailure, "达到上限的消息不再自动重试，必须由人工或补偿工具处理。");
    }

    /// <summary>
    /// 至少一次投递：发布成功但标记失败（此时崩溃），消息会被再次发布；消费端靠 Inbox 保证业务只发生一次。
    /// </summary>
    [TestMethod]
    public async Task PublishedButNotMarked_IsPublishedAgainAndTheConsumerStaysIdempotent()
    {
        var bus = new FakeEventBus();
        var message = new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = "acct-5", Amount = 12m };
        await AddPendingAsync("acct-5", message);

        await using var publisher = _harness.NewContext();
        IOutboxStore store = publisher;
        var firstLock = Guid.NewGuid();
        var firstBatch = await store.ClaimPendingAsync(firstLock, 10, 5, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        await bus.PublishAsync(message);
        // 崩溃点：消息已经出去了，但 MarkPublished 没来得及提交。

        var later = Now.AddMinutes(2);
        var secondLock = Guid.NewGuid();
        var secondBatch = await store.ClaimPendingAsync(secondLock, 10, 5, later, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.HasCount(1, secondBatch);
        await bus.PublishAsync(message);
        secondBatch[0].MarkPublished(secondLock, later);
        await store.SaveChangesAsync(CancellationToken.None);

        Assert.HasCount(2, bus.Published, "Outbox 是至少一次投递，重复发布是正常现象。");
        Assert.AreNotEqual(firstLock, secondLock);

        await using var consumerDb = _harness.NewContext();
        Assert.IsTrue(await MessageConsumer.ConsumeAsync(consumerDb, "ledger.reporting.v1", message));
        Assert.IsFalse(await MessageConsumer.ConsumeAsync(consumerDb, "ledger.reporting.v1", message));
        Assert.AreEqual(1, await consumerDb.LedgerEntries.CountAsync(value => value.Id == message.EntryId));
    }

    /// <summary>Stores the row with an explicit timestamp so the claim window is driven by the test clock, not by wall time.</summary>
    private async Task AddPendingAsync(string account, LedgerEntryRecordedIntegrationEvent? message = null)
    {
        var integrationEvent = message ?? new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = account, Amount = 1m };
        await using var db = _harness.NewContext();
        IOutboxStore outbox = db;
        await using var transaction = await db.BeginTransactionAsync();
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            System.Text.Json.JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            Now.AddMinutes(-1)));
        await db.CommitTransactionAsync(transaction);
    }
}
