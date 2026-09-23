using Microsoft.EntityFrameworkCore;
using MSA.Repositories.Persistence;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：至少一次投递带来的重复消费、毒消息回滚后的重放、以及同一条消息被多个消费者各自处理。
/// </summary>
[TestClass]
public sealed class InboxIdempotencyTests
{
    private const string Consumer = "ledger.recorder.v1";

    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>重复投递同一条消息：业务只执行一次。</summary>
    [TestMethod]
    public async Task DuplicateDeliveryAppliesTheBusinessChangeOnce()
    {
        var message = NewMessage("acct-1", 50m);
        await using var db = _harness.NewContext();

        Assert.IsTrue(await MessageConsumer.ConsumeAsync(db, Consumer, message), "首次投递应当被处理。");
        Assert.IsFalse(await MessageConsumer.ConsumeAsync(db, Consumer, message), "重复投递必须被 Inbox 挡住。");
        Assert.IsFalse(await MessageConsumer.ConsumeAsync(db, Consumer, message));

        Assert.AreEqual(1, await db.LedgerEntries.CountAsync());
        Assert.AreEqual(1, await db.InboxMessages.CountAsync());
    }

    /// <summary>同一条消息的不同消费者互不影响：去重键是「消息 + 消费者」，不是消息本身。</summary>
    [TestMethod]
    public async Task TheSameMessageIsProcessedOncePerConsumer()
    {
        var message = NewMessage("acct-2", 20m);
        await using var db = _harness.NewContext();

        Assert.IsTrue(await MessageConsumer.ConsumeAsync(db, "ledger.recorder.v1", message));
        Assert.IsTrue(await MessageConsumer.ConsumeAsync(
            db,
            "ledger.reporting.v1",
            message,
            context => { context.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), "reporting", message.Amount)); return Task.CompletedTask; }));

        Assert.AreEqual(2, await db.LedgerEntries.CountAsync(), "两个消费者各自完成了自己的副作用。");
        Assert.AreEqual(2, await db.InboxMessages.CountAsync());
    }

    /// <summary>
    /// 毒消息：处理中抛异常时整个事务回滚，Inbox 不留痕，消息进入死信队列后重放仍然可以成功。
    /// </summary>
    [TestMethod]
    public async Task FailedHandlerLeavesNothingBehindAndTheReplaySucceeds()
    {
        var message = NewMessage("acct-3", 75m);
        await using var db = _harness.NewContext();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => MessageConsumer.ConsumeAsync(
            db,
            Consumer,
            message,
            context =>
            {
                context.LedgerEntries.Add(new LedgerEntry(message.EntryId, message.Account, message.Amount));
                throw new InvalidOperationException("downstream call failed");
            }));

        Assert.IsFalse(await db.LedgerEntries.AnyAsync(), "失败的处理不能留下半成品。");
        Assert.IsFalse(await db.InboxMessages.AnyAsync(), "Inbox 没有登记，消息重放时才能再次处理。");

        await using var replay = _harness.NewContext();
        Assert.IsTrue(await MessageConsumer.ConsumeAsync(replay, Consumer, message), "死信重放应当能够正常消费。");
        Assert.AreEqual(1, await replay.LedgerEntries.CountAsync());
    }

    /// <summary>
    /// 并发重复投递：Inbox 主键是最后一道防线。第二个实例在第一个提交之前读过 Inbox，于是带着「可以处理」的
    /// 结论走到写入，提交时被主键拒绝，消息重投后会被正常去重，业务副作用仍然只发生一次。
    /// Postgres 上 TryBeginMessageAsync 走 INSERT ... ON CONFLICT DO NOTHING，落败方直接返回 false，不会抛异常。
    /// </summary>
    [TestMethod]
    public async Task ConcurrentDuplicateDeliveryIsRejectedByTheInboxPrimaryKey()
    {
        var message = NewMessage("acct-4", 10m);
        await using var first = _harness.NewContext();
        Assert.IsTrue(await MessageConsumer.ConsumeAsync(first, Consumer, message));

        await using var racing = _harness.NewContext();
        await using var transaction = await racing.BeginTransactionAsync();
        racing.InboxMessages.Add(new InboxMessage(message.Id, Consumer, DateTimeOffset.UtcNow));
        racing.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), message.Account, message.Amount));
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => racing.CommitTransactionAsync(transaction));

        await using var verify = _harness.NewContext();
        Assert.AreEqual(1, await verify.LedgerEntries.CountAsync(), "并发重复投递后账目仍然只有一条。");
        Assert.AreEqual(1, await verify.InboxMessages.CountAsync());
        Assert.IsFalse(await MessageConsumer.ConsumeAsync(verify, Consumer, message), "重投时会被正常去重。");
    }

    /// <summary>Inbox 记录与业务数据同一个事务：业务失败时「已消费」标记不会先行生效。</summary>
    [TestMethod]
    public async Task InboxRowIsCommittedTogetherWithTheBusinessChange()
    {
        var message = NewMessage("acct-5", 5m);
        await using var db = _harness.NewContext();

        await using (var transaction = await db.BeginTransactionAsync())
        {
            Assert.IsTrue(await db.TryBeginMessageAsync(message.Id, Consumer, CancellationToken.None));
            db.LedgerEntries.Add(new LedgerEntry(message.EntryId, message.Account, message.Amount));
            await db.RollbackTransactionAsync();
        }

        await using var verify = _harness.NewContext();
        Assert.IsFalse(await verify.InboxMessages.AnyAsync());
        Assert.IsFalse(await verify.LedgerEntries.AnyAsync());
    }

    private static LedgerEntryRecordedIntegrationEvent NewMessage(string account, decimal amount) =>
        new() { EntryId = Guid.NewGuid(), Account = account, Amount = amount };
}
