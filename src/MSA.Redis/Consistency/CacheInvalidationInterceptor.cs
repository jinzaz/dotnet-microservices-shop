using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MSA.Redis;

/// <summary>
/// 「实体变了 → 哪些缓存要失效」的映射，每个服务在启动时声明一次。
/// 规则按实体类型匹配（含子类），新增、修改、删除都会触发。
/// </summary>
public sealed class CacheInvalidationRules
{
    private readonly List<(Type EntityType, Action<object, CacheChange> Map)> _rules = [];

    public CacheInvalidationRules On<TEntity>(Action<TEntity, CacheChange> map)
        where TEntity : class
    {
        _rules.Add((typeof(TEntity), (entity, change) => map((TEntity)entity, change)));
        return this;
    }

    internal void Apply(object entity, CacheChange change)
    {
        foreach (var (entityType, map) in _rules)
        {
            if (entityType.IsInstanceOfType(entity))
                map(entity, change);
        }
    }
}

/// <summary>
/// 缓存与数据库一致性的落点：保存时按规则收集受影响的键，<b>事务提交之后</b>才删除缓存、推进代际、发变更通知；
/// 回滚或保存失败则整批丢弃。
/// <para>
/// 为什么是「先提交再删」：提交前删除，并发读者会在提交前把旧行重新写回缓存，直到过期都是脏的。
/// 提交后删除仍有一个窄窗口（读者在提交前读到旧行、在删除后才写回），由缓存库的代际校验把这次写回拒掉。
/// 两者合起来，旧值能被读到的时间只剩「提交到删除」之间的毫秒级，不需要延迟双删这类靠猜时长的补丁。
/// </para>
/// 挂在 EF 的保存与事务拦截点上，业务代码、MediatR 管道、集成事件处理器都不需要记得去失效。
/// </summary>
public sealed class CacheInvalidationInterceptor(
    CacheInvalidationRules rules,
    ICacheStore cache,
    IChangeFeed changes,
    ILogger<CacheInvalidationInterceptor> logger) : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, CacheChange> _pending = new();

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (IsAutoCommitted(eventData.Context))
            FlushAsync(eventData.Context!).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        // 没有显式事务时 SaveChanges 自带的事务已经提交；有显式事务则等它提交。
        if (IsAutoCommitted(eventData.Context))
            await FlushAsync(eventData.Context!);
        return result;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (IsAutoCommitted(eventData.Context))
            Discard(eventData.Context);
    }

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            FlushAsync(context).GetAwaiter().GetResult();
    }

    public async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            await FlushAsync(context);
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Discard(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        Discard(eventData.Context);

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
            return;

        // SavingChanges 在 EF 自动检测变更之前触发，这里先检测一遍，才能看到本次真正要落库的实体。
        context.ChangeTracker.DetectChanges();
        CacheChange? change = null;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            change ??= _pending.GetValue(context, _ => new CacheChange());
            rules.Apply(entry.Entity, change);
        }
    }

    private async Task FlushAsync(DbContext context)
    {
        if (!_pending.TryGetValue(context, out var change))
            return;
        _pending.Remove(context);
        if (change.IsEmpty)
            return;

        // 事务已经提交，调用方的取消不能再阻止缓存修复；失败时短 TTL 兜底。
        try
        {
            await cache.InvalidateAsync(change, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Cache invalidation after commit failed for {Keys}; entries will expire on their TTL.",
                string.Join(", ", change.Keys));
        }

        foreach (var topic in change.Topics)
        {
            try
            {
                await changes.PublishAsync(topic, cancellationToken: CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Change notification for {Topic} failed; subscribers will catch up on refresh.", topic);
            }
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
            _pending.Remove(context);
    }

    private static bool IsAutoCommitted(DbContext? context) =>
        context is not null && context.Database.CurrentTransaction is null;
}
