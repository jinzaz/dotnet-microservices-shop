using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSA.Persistence.Postgres;
using MSA.Repositories.Persistence;
using Npgsql;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 场景：PostgreSQL 封装本身。行锁子句的生成与追加不依赖数据库，可以离线验证；
/// Upsert 与咨询锁的真实行为在有数据库时验证。
/// </summary>
[TestClass]
public sealed class PostgresPersistenceTests
{
    private MessagingHarness _harness = null!;

    [TestInitialize]
    public void Initialize() => _harness = new MessagingHarness();

    [TestCleanup]
    public void Cleanup() => _harness.Dispose();

    /// <summary>LINQ 里的 ForUpdate 会变成查询标签，最终由拦截器翻译成锁子句，而不是手写整条 SQL。</summary>
    [TestMethod]
    public void RowLockIsCarriedByTheQueryAndAppendedToTheStatement()
    {
        using var db = _harness.NewContext();

        var sql = db.OutboxMessages
            .Where(message => message.Status == MSA.EventBusOutbox.OutboxStatus.Pending)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(10)
            .ForUpdateSkipLocked()
            .ToQueryString();

        StringAssert.Contains(sql, "msa-row-lock:", "行锁以标签形式挂在查询上。");
        Assert.AreEqual("FOR UPDATE SKIP LOCKED", AppendedClause(sql), "拦截器把标签翻译成 SKIP LOCKED。");
        // 锁子句必须在 LIMIT 之后，否则 PostgreSQL 会语法报错。
        var rewritten = Rewrite(sql);
        Assert.IsTrue(
            rewritten.LastIndexOf("LIMIT", StringComparison.Ordinal) < rewritten.LastIndexOf("FOR UPDATE", StringComparison.Ordinal),
            "锁子句应当追加在 LIMIT 之后。");
    }

    [TestMethod]
    public void EveryLockStrengthAndWaitModeMapsToItsClause()
    {
        using var db = _harness.NewContext();

        Assert.AreEqual("FOR UPDATE", ClauseOf(db, RowLockStrength.Update, RowLockWait.Wait));
        Assert.AreEqual("FOR UPDATE NOWAIT", ClauseOf(db, RowLockStrength.Update, RowLockWait.NoWait));
        Assert.AreEqual("FOR NO KEY UPDATE", ClauseOf(db, RowLockStrength.NoKeyUpdate, RowLockWait.Wait));
        Assert.AreEqual("FOR SHARE SKIP LOCKED", ClauseOf(db, RowLockStrength.Share, RowLockWait.SkipLocked));
        Assert.AreEqual("FOR KEY SHARE", ClauseOf(db, RowLockStrength.KeyShare, RowLockWait.Wait));
    }

    /// <summary>没有打标签的查询不能被改写；非 PostgreSQL 连接也必须原样放行。</summary>
    [TestMethod]
    public void UntaggedQueriesAndOtherProvidersAreLeftAlone()
    {
        const string plain = "SELECT * FROM ledger_entries";
        Assert.AreEqual(plain, Rewrite(plain));

        using var sqlite = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        using var command = sqlite.CreateCommand();
        command.CommandText = "-- msa-row-lock:0:1\n\nSELECT * FROM ledger_entries";
        Interceptor.ReaderExecuting(command, EventData(), default);
        Assert.IsFalse(command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal), "SQLite 上不加锁子句。");
    }

    /// <summary>同一条语句被拦截两次也不会追加两遍。</summary>
    [TestMethod]
    public void RewritingIsIdempotent()
    {
        var once = Rewrite("-- msa-row-lock:0:0\n\nSELECT * FROM ledger_entries LIMIT 1");
        Assert.AreEqual(once, Rewrite(once));
    }

    /// <summary>Upsert：冲突时什么也不做，返回值告诉调用方这次有没有真的插入。</summary>
    [TestMethod]
    public async Task InsertOnConflictDoNothingReportsWhetherItInserted()
    {
        await using var db = _harness.NewContext();
        var messageId = Guid.NewGuid();

        Assert.IsTrue(await db.InboxMessages.InsertOnConflictDoNothingAsync(
            new InboxMessage(messageId, "consumer-a", DateTimeOffset.UtcNow)));
        Assert.IsFalse(await db.InboxMessages.InsertOnConflictDoNothingAsync(
            new InboxMessage(messageId, "consumer-a", DateTimeOffset.UtcNow)), "重复的键不会再插入。");
        Assert.IsTrue(await db.InboxMessages.InsertOnConflictDoNothingAsync(
            new InboxMessage(messageId, "consumer-b", DateTimeOffset.UtcNow)), "复合主键的另一半不同，仍然是新行。");

        Assert.AreEqual(2, await db.InboxMessages.CountAsync());
    }

    /// <summary>Upsert：冲突时按 EXCLUDED 更新指定列，其余列保持不变。</summary>
    [TestMethod]
    public async Task UpsertUpdatesTheSelectedColumnsOnConflict()
    {
        await using var db = _harness.NewContext();
        var id = Guid.NewGuid();
        await db.LedgerEntries.UpsertAsync(new LedgerEntry(id, "acct-1", 10m));

        await db.LedgerEntries.UpsertAsync(
            new LedgerEntry(id, "acct-changed", 99m),
            updatedProperties: entry => entry.Amount);

        var stored = await db.LedgerEntries.AsNoTracking().SingleAsync(entry => entry.Id == id);
        Assert.AreEqual(99m, stored.Amount, "指定的列被更新。");
        Assert.AreEqual("acct-1", stored.Account, "没有指定的列保持原值。");
    }

    /// <summary>咨询锁必须在事务里用，否则拿到锁的瞬间就释放了，等于没锁。</summary>
    [TestMethod]
    public async Task AdvisoryLocksRequireATransaction()
    {
        await using var db = _harness.NewContext();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => db.AcquireTransactionLockAsync("ledger:acct-1"));
    }

    /// <summary>真实数据库上：同一把咨询锁不会被两个事务同时拿到。</summary>
    [TestMethod]
    public async Task AnAdvisoryLockIsHeldUntilTheTransactionEnds()
    {
        if (!_harness.IsPostgres)
            Assert.Inconclusive("咨询锁是 PostgreSQL 特性，需要配置可用的测试数据库。");

        var key = $"ledger:{Guid.NewGuid():N}";
        await using var holder = _harness.NewContext();
        await using var contender = _harness.NewContext();

        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        await holder.AcquireTransactionLockAsync(key);

        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync())
        {
            Assert.IsFalse(await contender.TryAcquireTransactionLockAsync(key), "锁被别人持有时不能拿到。");
        }

        await holderTransaction.RollbackAsync();

        await using var afterRelease = await contender.Database.BeginTransactionAsync();
        Assert.IsTrue(await contender.TryAcquireTransactionLockAsync(key), "持有者的事务结束后锁应当释放。");
    }

    /// <summary>
    /// 真实数据库上：两个并发 worker 用 SKIP LOCKED 各自拿到不同的行，而不是互相等待。
    /// 优惠券配额分桶、Outbox 领取都依赖这条路径。
    /// </summary>
    [TestMethod]
    public async Task SkipLockedHandsCompetingWorkersDifferentRows()
    {
        if (!_harness.IsPostgres)
            Assert.Inconclusive("SKIP LOCKED 是 PostgreSQL 特性，需要配置可用的测试数据库。");

        await using (var seed = _harness.NewContext())
        {
            seed.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), "bucket-1", 1m));
            seed.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), "bucket-2", 1m));
            await seed.SaveChangesAsync();
        }

        await using var first = _harness.NewContext();
        await using var second = _harness.NewContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();

        var claimedByFirst = await Claim(first);
        var claimedBySecond = await Claim(second);

        Assert.IsNotNull(claimedByFirst);
        Assert.IsNotNull(claimedBySecond);
        Assert.AreNotEqual(claimedByFirst, claimedBySecond, "第二个 worker 必须跳过已锁住的行，拿到另一行。");

        await using var third = _harness.NewContext();
        await using var thirdTransaction = await third.Database.BeginTransactionAsync();
        Assert.IsNull(await Claim(third), "两行都被锁住时，第三个 worker 什么也拿不到而不是阻塞。");

        // 取可空 Guid：拿不到行时要能和 Guid.Empty 区分开。
        static Task<Guid?> Claim(LedgerDbContext db) =>
            db.LedgerEntries
                .OrderBy(entry => entry.Account)
                .Select(entry => (Guid?)entry.Id)
                .ForUpdateSkipLocked()
                .FirstOrDefaultAsync();
    }

    private static PostgresRowLockingInterceptor Interceptor => PostgresRowLockingInterceptor.Instance;

    private static string ClauseOf(LedgerDbContext db, RowLockStrength strength, RowLockWait wait) =>
        AppendedClause(db.LedgerEntries.WithRowLock(strength, wait).ToQueryString());

    /// <summary>跑一遍真实的拦截器（Npgsql 连接对象即可，不需要连上服务器），返回它追加的子句。</summary>
    private static string? AppendedClause(string sql)
    {
        var rewritten = Rewrite(sql);
        return rewritten == sql ? null : rewritten[(sql.TrimEnd().TrimEnd(';').Length + Environment.NewLine.Length)..];
    }

    private static string Rewrite(string sql)
    {
        using var connection = new NpgsqlConnection("Host=localhost;Database=unused");
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Interceptor.ReaderExecuting(command, EventData(), default);
        return command.CommandText;
    }

    private static CommandEventData EventData() => null!;
}
