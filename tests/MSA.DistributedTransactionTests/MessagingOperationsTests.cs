using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;
using MSA.Repositories.Persistence;
using MSA.Saga;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：失败消息与卡住流程的运维出口，以及 Outbox/Inbox 的保留期清理。
/// 这些是「最终一致」真正需要人介入时的最后一环。
/// </summary>
[TestClass]
public sealed class MessagingOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>重试耗尽的消息可以被列出，并在修复根因后重新排队发布。</summary>
    [TestMethod]
    public async Task FailedOutboxMessagesCanBeListedAndRequeued()
    {
        await using var db = _harness.NewContext();
        IOutboxStore store = db;
        IMessagingOperations operations = db;
        await AddPendingAsync(db, Now.AddMinutes(-1));

        const int maxRetries = 1;
        var lockId = Guid.NewGuid();
        var batch = await store.ClaimPendingAsync(lockId, 10, maxRetries, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        batch[0].MarkFailed(lockId, new InvalidOperationException("broker down"), Now, maxRetries);
        await store.SaveChangesAsync(CancellationToken.None);

        var failed = await operations.ListFailedOutboxAsync(50, CancellationToken.None);
        Assert.HasCount(1, failed);
        Assert.Contains("broker down", failed[0].LastError!);

        var requeued = await operations.RequeueFailedOutboxAsync(failed[0].Id, 10, Now, CancellationToken.None);
        Assert.AreEqual(1, requeued);

        var claimable = await store.ClaimPendingAsync(Guid.NewGuid(), 10, maxRetries, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.HasCount(1, claimable, "重投之后消息必须重新可被发布。");
        Assert.AreEqual(0, claimable[0].RetryCount, "重试预算需要重置，否则立刻又会被判定为失败。");
    }

    /// <summary>失败的 Saga 和长时间没有推进的 Saga 都能被查出来，正常推进中的不会误报。</summary>
    [TestMethod]
    public async Task StalledAndFailedSagasAreVisible()
    {
        await using var db = _harness.NewContext();
        ISagaStore sagas = db;
        IMessagingOperations operations = db;

        var failed = SagaState.Start(Guid.NewGuid(), "OrderFulfillment", "CompensatingRefund", "{}", Now.AddMinutes(-90));
        sagas.Add(failed);
        failed.Fail("Payment refund requires manual intervention: gateway rejected", Now.AddMinutes(-60));

        var stalled = SagaState.Start(Guid.NewGuid(), "OrderFulfillment", "AwaitingPayment", "{}", Now.AddHours(-3));
        sagas.Add(stalled);

        var healthy = SagaState.Start(Guid.NewGuid(), "OrderFulfillment", "AwaitingPayment", "{}", Now.AddMinutes(-70));
        sagas.Add(healthy);
        healthy.TransitionTo("AwaitingInventoryCommit", "{}", Now.AddMinutes(-1));
        await sagas.SaveChangesAsync();

        var rows = await operations.ListStalledSagasAsync(Now.AddMinutes(-30), 50, CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { failed.CorrelationId, stalled.CorrelationId },
            rows.Select(row => row.CorrelationId).ToArray());
        Assert.Contains("manual intervention", rows.Single(row => row.CorrelationId == failed.CorrelationId).FailureReason!);
    }

    /// <summary>保留期清理只删掉已发布且过期的记录，未发布的消息和仍在去重窗口内的 Inbox 记录必须留着。</summary>
    [TestMethod]
    public async Task RetentionKeepsWhatIsStillNeeded()
    {
        await using var db = _harness.NewContext();
        IOutboxStore store = db;
        IMessagingOperations operations = db;

        var oldPublished = await AddPendingAsync(db, Now.AddDays(-30));
        var recentPublished = await AddPendingAsync(db, Now.AddHours(-1));
        await AddPendingAsync(db, Now.AddDays(-30)); // 仍未发布：无论多老都不能删
        foreach (var (message, processedOn) in new[] { (oldPublished, Now.AddDays(-30)), (recentPublished, Now.AddHours(-1)) })
        {
            var lockId = Guid.NewGuid();
            message.Claim(lockId, processedOn.AddMinutes(1));
            message.MarkPublished(lockId, processedOn);
        }

        db.InboxMessages.Add(new InboxMessage(Guid.NewGuid(), "ledger.recorder.v1", Now.AddDays(-60)));
        db.InboxMessages.Add(new InboxMessage(Guid.NewGuid(), "ledger.recorder.v1", Now.AddDays(-1)));
        await store.SaveChangesAsync(CancellationToken.None);

        var (outbox, inbox) = await operations.PurgeProcessedMessagesAsync(
            Now.AddDays(-7),
            Now.AddDays(-30),
            1000,
            CancellationToken.None);

        Assert.AreEqual(1, outbox);
        Assert.AreEqual(1, inbox);
        await using var verify = _harness.NewContext();
        Assert.AreEqual(2, await verify.OutboxMessages.CountAsync(), "未发布的消息和最近发布的记录都要保留。");
        Assert.AreEqual(1, await verify.InboxMessages.CountAsync(), "去重窗口内的 Inbox 记录不能删，否则重复消息会被重放。");
        Assert.AreEqual(1, await verify.OutboxMessages.CountAsync(message => message.Status == OutboxStatus.Pending));
    }

    private static async Task<OutboxMessage> AddPendingAsync(LedgerDbContext db, DateTimeOffset occurredOn)
    {
        var integrationEvent = new LedgerEntryRecordedIntegrationEvent { EntryId = Guid.NewGuid(), Account = "acct", Amount = 1m };
        var message = OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            System.Text.Json.JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            occurredOn);
        ((IOutboxStore)db).Add(message);
        await ((IOutboxStore)db).SaveChangesAsync(CancellationToken.None);
        return message;
    }
}
