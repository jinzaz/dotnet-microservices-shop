using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MSA.EventBus.Abstractions;
using MSA.Repositories.Persistence;

namespace MSA.DistributedTransactionTests;

/// <summary>A business row the sample consumers write, so "did the side effect happen exactly once?" is observable.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
    }

    public LedgerEntry(Guid id, string account, decimal amount)
    {
        Id = id;
        Account = account;
        Amount = amount;
    }

    public Guid Id { get; private set; }
    public string Account { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
}

/// <summary>
/// A minimal service database built from the same <see cref="MessagingDbContext"/> the real services use, so the
/// tests exercise the production inbox, outbox and saga store rather than a stand-in.
/// </summary>
public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator, "ledger")
{
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    private bool IsSqlite => Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;

    /// <summary>
    /// SQLite cannot compare or order the TEXT form of DateTimeOffset, which the lease queries rely on; storing the
    /// binary form keeps those queries translatable. On Npgsql the production mapping is used unchanged.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        if (IsSqlite)
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 共享的测试库里用独立 schema，避免和别的表混在一起，也方便一次性清理。
        if (!IsSqlite)
            modelBuilder.HasDefaultSchema(TestEnvironment.Schema);
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("ledger_entries");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Account).HasMaxLength(100).IsRequired();
        });
    }
}

public sealed class LedgerEntryRecordedIntegrationEvent : IntegrationEvent
{
    public Guid EntryId { get; init; }
    public string Account { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

/// <summary>Records what the bus was asked to publish, and can fail a configurable number of times first.</summary>
public sealed class FakeEventBus : IEventBus
{
    private readonly Lock _gate = new();
    private int _failuresLeft;

    public List<IntegrationEvent> Published { get; } = [];

    public void FailNextPublishes(int count) => _failuresLeft = count;

    /// <summary>满足条件的事件第一次发布时失败，之后成功；用于并发发布时只让指定的几条失败。</summary>
    public Func<IntegrationEvent, bool>? FailOnceWhen { get; set; }

    private readonly HashSet<Guid> _failedOnce = [];

    public Task PublishAsync(IntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                return Task.FromException(new InvalidOperationException("Broker unreachable."));
            }
            if (FailOnceWhen?.Invoke(@event) == true && _failedOnce.Add(@event.Id))
                return Task.FromException(new InvalidOperationException("Broker nacked the message."));

            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// 数据库夹具。配置了 Postgres（testsettings.json / MSA_TEST_POSTGRES）时用真实的 Postgres，
/// 跑的就是生产的 SKIP LOCKED、ON CONFLICT 和行锁路径；否则退回本地 SQLite 文件库。
/// 多个上下文 = 多个连接 = 多个「实例」，竞争消费者与乐观并发冲突才可复现。
/// </summary>
public sealed class MessagingHarness : IDisposable
{
    private static readonly Lock SchemaGate = new();
    private static bool _schemaReady;

    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), $"msa-dtx-{Guid.NewGuid():N}.db");
    private readonly List<LedgerDbContext> _contexts = [];

    public MessagingHarness()
    {
        if (TestEnvironment.UsePostgres)
        {
            EnsurePostgresSchema();
            ClearPostgresTables();
            return;
        }

        using var context = NewContext();
        context.Database.EnsureCreated();
        // WAL keeps a reader from blocking the writer, which is what the competing-worker tests need.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public bool IsPostgres => TestEnvironment.UsePostgres;

    /// <summary>A fresh context means a fresh connection, change tracker and transaction — one "worker".</summary>
    public LedgerDbContext NewContext()
    {
        var context = new LedgerDbContext(Options(), Mock.Of<IMediator>());
        lock (SchemaGate)
            _contexts.Add(context);
        return context;
    }

    /// <summary>Registers the same context factory in a container, for tests that run the real hosted services.</summary>
    public void AddContext(IServiceCollection services) => services.AddScoped(_ => NewContext());

    private DbContextOptions<LedgerDbContext> Options()
    {
        var builder = new DbContextOptionsBuilder<LedgerDbContext>();
        if (TestEnvironment.UsePostgres)
            builder.UseNpgsql(PostgresConnectionString());
        else
            builder.UseSqlite($"Data Source={_sqlitePath}");
        return builder.Options;
    }

    /// <summary>
    /// 把 search_path 指向测试 schema：生产代码里的 FOR UPDATE SKIP LOCKED、ON CONFLICT 等原生 SQL 用的是
    /// 不带 schema 的表名，只有这样测试才跑在和生产完全一致的语句上。
    /// </summary>
    private static string PostgresConnectionString() =>
        new Npgsql.NpgsqlConnectionStringBuilder(TestEnvironment.PostgresConnectionString)
        {
            SearchPath = TestEnvironment.Schema
        }.ConnectionString;

    private void EnsurePostgresSchema()
    {
        lock (SchemaGate)
        {
            if (_schemaReady)
                return;
            using var context = new LedgerDbContext(Options(), Mock.Of<IMediator>());
            context.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS \"{TestEnvironment.Schema}\";");
            // EnsureCreated 在库里已有表时什么也不做，因此直接按模型建表（幂等：表已存在就跳过）。
            var creator = (RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>();
            if (!creator.HasTables() || !TablesExist(context))
                CreateMissingTables(creator, context);
            _schemaReady = true;
        }
    }

    private static bool TablesExist(LedgerDbContext context) =>
        context.Database.SqlQueryRaw<int>(
                $"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = '{TestEnvironment.Schema}'")
            .AsEnumerable()
            .First() >= 4;

    private static void CreateMissingTables(RelationalDatabaseCreator creator, LedgerDbContext context)
    {
        try
        {
            creator.CreateTables();
        }
        catch (Exception exception) when (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // 另一个进程刚建过：忽略即可。
            _ = context;
        }
    }

    /// <summary>Each test starts from an empty schema; the shared test database is reused, not recreated.</summary>
    private void ClearPostgresTables()
    {
        using var context = new LedgerDbContext(Options(), Mock.Of<IMediator>());
        context.Database.ExecuteSqlRaw(
            $"""
            TRUNCATE TABLE
                "{TestEnvironment.Schema}"."ledger_entries",
                "{TestEnvironment.Schema}"."ledger_inbox_messages",
                "{TestEnvironment.Schema}"."ledger_outbox_messages",
                "{TestEnvironment.Schema}"."ledger_saga_states"
            RESTART IDENTITY CASCADE;
            """);
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();
        if (TestEnvironment.UsePostgres)
            return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_sqlitePath))
            File.Delete(_sqlitePath);
    }
}

/// <summary>
/// The shape every service handler uses: one local transaction that holds the inbox row, the business change and the
/// outbox rows together. Tests drive it to reproduce duplicate, out-of-order and failing deliveries.
/// </summary>
public static class MessageConsumer
{
    /// <summary>Returns false when the message was already consumed by this consumer (duplicate delivery).</summary>
    public static async Task<bool> ConsumeAsync(
        LedgerDbContext db,
        string consumer,
        LedgerEntryRecordedIntegrationEvent message,
        Func<LedgerDbContext, Task>? apply = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(message.Id, consumer, cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            if (apply is not null)
                await apply(db);
            else
                db.LedgerEntries.Add(new LedgerEntry(message.EntryId, message.Account, message.Amount));

            await db.CommitTransactionAsync(transaction, cancellationToken);
            return true;
        }
        catch
        {
            // The real bus nacks here, so the broker moves the message to the dead-letter queue and nothing is kept.
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
