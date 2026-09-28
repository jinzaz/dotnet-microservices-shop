using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MSA.Persistence.Postgres;

/// <summary>
/// 事务级咨询锁（pg_advisory_xact_lock）。用业务键串行化没有对应数据行可锁的操作，
/// 例如「同一个用户的购物车改动」「同一个订单的库存预占」。锁随事务结束自动释放，不会泄漏。
/// </summary>
public static class PostgresAdvisoryLockExtensions
{
    /// <summary>按业务键加事务级排他锁，已被别人持有时等待。</summary>
    public static Task AcquireTransactionLockAsync(
        this DbContext context,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return context.AcquireTransactionLocksAsync([key], cancellationToken);
    }

    /// <summary>
    /// 一次获取多个键的锁。键会去重并排序后在一条语句里加锁——固定顺序是避免死锁的关键，
    /// 否则两个事务以相反顺序锁同样的两个键就会互相等待。
    /// </summary>
    public static async Task AcquireTransactionLocksAsync(
        this DbContext context,
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);
        var ordered = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            return;

        EnsureTransaction(context);
        await PostgresCommand.ExecuteNonQueryAsync(
            context,
            """
            SELECT pg_advisory_xact_lock(hashtextextended(key, 0))
            FROM unnest(@keys) AS locks(key)
            ORDER BY key
            """,
            command =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "keys";
                parameter.Value = ordered;
                command.Parameters.Add(parameter);
            },
            cancellationToken);
    }

    /// <summary>尝试加锁，拿不到立即返回 false，用于「同一资源已有人在处理就跳过」。</summary>
    public static async Task<bool> TryAcquireTransactionLockAsync(
        this DbContext context,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        EnsureTransaction(context);
        var acquired = await PostgresCommand.ExecuteScalarAsync(
            context,
            "SELECT pg_try_advisory_xact_lock(hashtextextended(@key, 0))",
            command =>
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "key";
                parameter.Value = key.Trim();
                command.Parameters.Add(parameter);
            },
            cancellationToken);
        return acquired is true;
    }

    private static void EnsureTransaction(DbContext context)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Advisory transaction locks require an active transaction; without one the lock is released immediately.");
        }
    }
}
