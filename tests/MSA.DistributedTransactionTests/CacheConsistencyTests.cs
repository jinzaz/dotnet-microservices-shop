using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.Redis;
using StackExchange.Redis;

namespace MSA.DistributedTransactionTests;

/// <summary>
/// 缓存与数据库「双写」的常见问题场景，跑在真实 Redis 上（testsettings.json 的 Redis，逻辑库 15）。
/// 每个用例一个随机键前缀，结束时删掉自己写的键。Redis 连不上时标记为 Inconclusive。
/// <list type="bullet">
/// <item>读写并发的脏回填：读者读到旧行，写者提交并删缓存，读者再把旧值写回去 —— 代际校验拒绝这次写回。</item>
/// <item>先删缓存再提交 / 回滚后误删：失效挂在事务提交上，提交前不删，回滚不删。</item>
/// <item>穿透：不存在的键缓存为空，数据库只被问一次。</item>
/// <item>击穿：热点键冷启动时并发读，只回源一次。</item>
/// <item>雪崩：同一批写入的键过期时间带抖动，不会同一刻一起失效。</item>
/// <item>列表类缓存：命名空间代际一推进，所有分页一起作废。</item>
/// <item>Redis 故障：读直接回源，业务照常。</item>
/// <item>分布式锁：同一时刻只有一个持有者，过期的旧持有者不能删掉新持有者的锁。</item>
/// </list>
/// </summary>
[TestClass]
public sealed class CacheConsistencyTests
{
    private static readonly CachePolicy Policy = new(TimeSpan.FromMinutes(5)) { Jitter = 0 };

    private IConnectionMultiplexer _redis = null!;
    private RedisKeyspace _keyspace = null!;
    private RedisCacheStore _cache = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Connect()
    {
        if (!TestEnvironment.RedisReachable)
            Assert.Inconclusive("Redis is not reachable; configure testsettings.json Redis or MSA_TEST_Redis.");
        _redis = ConnectionMultiplexer.Connect(TestEnvironment.RedisConnectionString!);
        _keyspace = new RedisKeyspace($"dtx-{Guid.NewGuid():N}");
        _cache = Store(_redis, _keyspace);
    }

    [TestCleanup]
    public async Task CleanUp()
    {
        if (_redis is null)
            return;
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var database = _redis.GetDatabase();
        await foreach (var key in server.KeysAsync(database.Database, $"*{_keyspace.Prefix}*"))
            await database.KeyDeleteAsync(key);
        await _redis.DisposeAsync();
    }

    [TestMethod]
    public async Task StaleFill_AfterAConcurrentWriterInvalidated_IsRejected()
    {
        var loads = 0;
        // 读者从库里读到 v1 的这段时间里，写者提交了 v2 并删了缓存。
        var first = await _cache.GetOrLoadAsync("row:1", async _ =>
        {
            Interlocked.Increment(ref loads);
            await _cache.InvalidateAsync(new CacheChange().Key("row:1"));
            return new Row("v1");
        }, Policy, TestContext.CancellationToken);

        // 读者这次照样拿到它读到的值，但这个旧值不能留在缓存里；下一次读必须回源拿到 v2。
        var second = await _cache.GetOrLoadAsync("row:1", _ =>
        {
            Interlocked.Increment(ref loads);
            return Task.FromResult<Row?>(new Row("v2"));
        }, Policy, TestContext.CancellationToken);

        Assert.AreEqual("v1", first!.Value);
        Assert.AreEqual("v2", second!.Value);
        Assert.AreEqual(2, loads);
    }

    [TestMethod]
    public async Task Invalidation_WaitsForCommit_AndIsSkippedOnRollback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.CancellationToken);
        var rules = new CacheInvalidationRules().On<RowEntity>((row, change) => change.Key($"row:{row.Id}"));
        var interceptor = new CacheInvalidationInterceptor(
            rules, _cache, new NoChanges(), NullLogger<CacheInvalidationInterceptor>.Instance);
        await using var db = new RowContext(new DbContextOptionsBuilder<RowContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options);
        await db.Database.EnsureCreatedAsync(TestContext.CancellationToken);
        db.Rows.Add(new RowEntity { Id = 1, Value = "v1" });
        await db.SaveChangesAsync(TestContext.CancellationToken);
        await WarmAsync("row:1", "v1");

        // 回滚：什么都没改成，缓存里的值仍然正确，不能被删。
        await using (var rolledBack = await db.Database.BeginTransactionAsync(TestContext.CancellationToken))
        {
            (await db.Rows.SingleAsync(TestContext.CancellationToken)).Value = "never";
            await db.SaveChangesAsync(TestContext.CancellationToken);
            await rolledBack.RollbackAsync(TestContext.CancellationToken);
        }
        Assert.IsTrue(await CachedAsync("row:1"), "A rolled-back change must not evict the cache.");
        db.ChangeTracker.Clear();

        // 提交：保存之后、提交之前缓存还在（提前删会被并发读者用未提交前的旧行重新填上）；提交之后才删。
        await using (var committed = await db.Database.BeginTransactionAsync(TestContext.CancellationToken))
        {
            (await db.Rows.SingleAsync(TestContext.CancellationToken)).Value = "v2";
            await db.SaveChangesAsync(TestContext.CancellationToken);
            Assert.IsTrue(await CachedAsync("row:1"), "Eviction must wait for the commit.");
            await committed.CommitAsync(TestContext.CancellationToken);
        }
        Assert.IsFalse(await CachedAsync("row:1"), "The commit must evict the stale entry.");
    }

    [TestMethod]
    public async Task Penetration_AMissingRowIsCachedAsNull_SoTheDatabaseIsAskedOnce()
    {
        var loads = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var value = await _cache.GetOrLoadAsync<Row>("row:missing", _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<Row?>(null);
            }, Policy, TestContext.CancellationToken);
            Assert.IsNull(value);
        }

        Assert.AreEqual(1, loads);
    }

    [TestMethod]
    public async Task Breakdown_ConcurrentReadersOfAColdHotKey_LoadOnce()
    {
        var loads = 0;
        var readers = Enumerable.Range(0, 50).Select(_ => _cache.GetOrLoadAsync("row:hot", async _ =>
        {
            Interlocked.Increment(ref loads);
            await Task.Delay(200);
            return new Row("hot");
        }, Policy, TestContext.CancellationToken));

        var values = await Task.WhenAll(readers);

        Assert.IsTrue(values.All(value => value?.Value == "hot"));
        Assert.AreEqual(1, loads);
    }

    [TestMethod]
    public async Task Avalanche_KeysWrittenTogetherDoNotExpireTogether()
    {
        var jittered = new CachePolicy(TimeSpan.FromMinutes(10)) { Jitter = 0.2 };
        for (var index = 0; index < 20; index++)
            await _cache.GetOrLoadAsync($"batch:{index}", _ => Task.FromResult<Row?>(new Row("x")), jittered, TestContext.CancellationToken);

        var ttls = new List<TimeSpan>();
        for (var index = 0; index < 20; index++)
            ttls.Add((await _redis.GetDatabase().KeyTimeToLiveAsync(_keyspace.Key($"batch:{index}")))!.Value);

        Assert.IsTrue(ttls.All(ttl => ttl > TimeSpan.FromMinutes(9) && ttl <= TimeSpan.FromMinutes(12)));
        Assert.IsGreaterThan(10, ttls.Select(ttl => (long)ttl.TotalSeconds).Distinct().Count(),
            "Expirations should be spread out, not stacked on the same second.");
    }

    [TestMethod]
    public async Task ListNamespace_OneBumpInvalidatesEveryPage()
    {
        var loads = 0;
        async Task<Row?> Page(int page)
        {
            var generation = await _cache.NamespaceAsync("product-list", TestContext.CancellationToken);
            return await _cache.GetOrLoadAsync($"{generation}:page:{page}", _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult<Row?>(new Row($"page-{page}"));
            }, Policy, TestContext.CancellationToken);
        }

        await Page(1);
        await Page(2);
        await Page(1);
        Assert.AreEqual(2, loads, "Warm pages are served from the cache.");

        await _cache.InvalidateAsync(new CacheChange().Namespace("product-list"), TestContext.CancellationToken);
        await Page(1);
        await Page(2);

        Assert.AreEqual(4, loads, "After the bump every page is rebuilt, without knowing which pages existed.");
    }

    [TestMethod]
    public async Task RedisOutage_ReadsFallBackToTheSource()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 200;
        options.SyncTimeout = 200;
        options.AsyncTimeout = 200;
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(options);
        var cache = Store(unreachable, _keyspace);

        var value = await cache.GetOrLoadAsync("row:1", _ => Task.FromResult<Row?>(new Row("from-db")), Policy, TestContext.CancellationToken);

        Assert.AreEqual("from-db", value!.Value);
    }

    [TestMethod]
    public async Task DistributedLock_OneHolderAtATime_AndAnExpiredHolderCannotReleaseTheNextOne()
    {
        var locks = new RedisDistributedLock(_redis, _keyspace, NullLogger<RedisDistributedLock>.Instance);

        var first = await locks.TryAcquireAsync("claim:u1", TimeSpan.FromMilliseconds(300), TestContext.CancellationToken);
        var contender = await locks.TryAcquireAsync("claim:u1", TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(first);
        Assert.IsNull(contender);

        // 第一个持有者超时了（租期到期），第二个拿到锁；第一个迟到的释放不能把第二个的锁删掉。
        await Task.Delay(500, TestContext.CancellationToken);
        var second = await locks.TryAcquireAsync("claim:u1", TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        Assert.IsNotNull(second);
        await first!.DisposeAsync();
        Assert.IsNull(await locks.TryAcquireAsync("claim:u1", TimeSpan.FromSeconds(5), TestContext.CancellationToken));

        await second!.DisposeAsync();
        Assert.IsNotNull(await locks.TryAcquireAsync("claim:u1", TimeSpan.FromSeconds(5), TestContext.CancellationToken));
    }

    private async Task WarmAsync(string key, string value) =>
        await _cache.GetOrLoadAsync(key, _ => Task.FromResult<Row?>(new Row(value)), Policy, TestContext.CancellationToken);

    private Task<bool> CachedAsync(string key) => _redis.GetDatabase().KeyExistsAsync(_keyspace.Key(key));

    private static RedisCacheStore Store(IConnectionMultiplexer redis, RedisKeyspace keyspace) =>
        new(redis, keyspace, new MemoryCache(new MemoryCacheOptions()), NullLogger<RedisCacheStore>.Instance);

    public sealed record Row(string Value);

    public sealed class RowEntity
    {
        public int Id { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    private sealed class RowContext(DbContextOptions<RowContext> options) : DbContext(options)
    {
        public DbSet<RowEntity> Rows => Set<RowEntity>();
    }

    private sealed class NoChanges : IChangeFeed
    {
        public Task PublishAsync(string topic, string message = "", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IAsyncDisposable> SubscribeAsync(string topic, Action<string> onMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
