using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MSA.Persistence.Postgres;

/// <summary>PostgreSQL 的行级锁强度，对应 SELECT ... FOR 子句。</summary>
public enum RowLockStrength
{
    /// <summary>FOR UPDATE：最强的行锁，阻塞其他写与加锁读。</summary>
    Update,

    /// <summary>FOR NO KEY UPDATE：不阻塞引用该行的外键插入，适合只改非键列。</summary>
    NoKeyUpdate,

    /// <summary>FOR SHARE：允许其他读锁，阻塞写。</summary>
    Share,

    /// <summary>FOR KEY SHARE：最弱，只阻塞改键值与删除。</summary>
    KeyShare
}

/// <summary>锁等待策略。</summary>
public enum RowLockWait
{
    /// <summary>等待锁释放（默认）。</summary>
    Wait,

    /// <summary>跳过已被锁住的行，用于多实例并行取任务。</summary>
    SkipLocked,

    /// <summary>行已被锁住时立即报错（55P03）。</summary>
    NoWait
}

/// <summary>
/// 行锁以查询标签的形式挂在 LINQ 查询上，由 <see cref="PostgresRowLockingInterceptor"/> 在命令执行前
/// 追加到 SQL 末尾。这样既能保留 EF 的 LINQ 组合能力，又不必手写整条 SQL。
/// </summary>
internal static class RowLockTag
{
    public const string Prefix = "msa-row-lock:";

    public static string Create(RowLockStrength strength, RowLockWait wait) => $"{Prefix}{(int)strength}:{(int)wait}";

    /// <summary>只解析我们自己写入的枚举值，不从标签里取任意 SQL 片段。</summary>
    public static string? FindClause(string commandText)
    {
        var index = commandText.IndexOf(Prefix, StringComparison.Ordinal);
        if (index < 0)
            return null;

        // 标签形如 "msa-row-lock:<strength>:<wait>"，后面跟着 EF 生成的换行与 SQL。
        var value = commandText.AsSpan(index + Prefix.Length);
        var separator = value.IndexOf(':');
        if (separator < 0)
            return null;
        var waitEnd = separator + 1;
        while (waitEnd < value.Length && char.IsAsciiDigit(value[waitEnd]))
            waitEnd++;

        return int.TryParse(value[..separator], out var strength)
            && int.TryParse(value[(separator + 1)..waitEnd], out var wait)
            && Enum.IsDefined((RowLockStrength)strength)
            && Enum.IsDefined((RowLockWait)wait)
                ? Clause((RowLockStrength)strength, (RowLockWait)wait)
                : null;
    }

    private static string Clause(RowLockStrength strength, RowLockWait wait)
    {
        var lockClause = strength switch
        {
            RowLockStrength.Update => "FOR UPDATE",
            RowLockStrength.NoKeyUpdate => "FOR NO KEY UPDATE",
            RowLockStrength.Share => "FOR SHARE",
            _ => "FOR KEY SHARE"
        };
        return wait switch
        {
            RowLockWait.SkipLocked => $"{lockClause} SKIP LOCKED",
            RowLockWait.NoWait => $"{lockClause} NOWAIT",
            _ => lockClause
        };
    }
}

public static class PostgresRowLockingQueryableExtensions
{
    /// <summary>
    /// SELECT ... FOR UPDATE。必须在事务中使用，锁在事务结束时释放。
    /// 只适用于单表查询：带 Include 的拆分查询请先锁住主表，或改用 AsSingleQuery。
    /// </summary>
    public static IQueryable<TEntity> ForUpdate<TEntity>(
        this IQueryable<TEntity> source,
        RowLockWait wait = RowLockWait.Wait) =>
        source.WithRowLock(RowLockStrength.Update, wait);

    /// <summary>SELECT ... FOR UPDATE SKIP LOCKED：多实例并行领取任务的标准写法。</summary>
    public static IQueryable<TEntity> ForUpdateSkipLocked<TEntity>(this IQueryable<TEntity> source) =>
        source.WithRowLock(RowLockStrength.Update, RowLockWait.SkipLocked);

    /// <summary>SELECT ... FOR SHARE。</summary>
    public static IQueryable<TEntity> ForShare<TEntity>(
        this IQueryable<TEntity> source,
        RowLockWait wait = RowLockWait.Wait) =>
        source.WithRowLock(RowLockStrength.Share, wait);

    public static IQueryable<TEntity> WithRowLock<TEntity>(
        this IQueryable<TEntity> source,
        RowLockStrength strength,
        RowLockWait wait = RowLockWait.Wait)
    {
        ArgumentNullException.ThrowIfNull(source);
        // 行锁只对实际读到的行有意义，因此顺便关闭该查询的缓存复用问题：标签本身已参与查询缓存键。
        return source.TagWith(RowLockTag.Create(strength, wait));
    }
}

/// <summary>
/// 把 <see cref="PostgresRowLockingQueryableExtensions"/> 打上的标签翻译成真正的锁子句。
/// 非 PostgreSQL 连接（例如测试用的 SQLite）直接忽略，查询照常执行，只是没有锁语义。
/// </summary>
public sealed class PostgresRowLockingInterceptor : DbCommandInterceptor
{
    public static PostgresRowLockingInterceptor Instance { get; } = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Apply(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Apply(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static void Apply(DbCommand command)
    {
        if (command.Connection is not Npgsql.NpgsqlConnection)
            return;
        var clause = RowLockTag.FindClause(command.CommandText);
        if (clause is null || command.CommandText.EndsWith(clause, StringComparison.Ordinal))
            return;

        // 锁子句必须在 LIMIT/OFFSET 之后，所以直接追加到语句末尾。
        command.CommandText = $"{command.CommandText.TrimEnd().TrimEnd(';')}{Environment.NewLine}{clause}";
    }
}
