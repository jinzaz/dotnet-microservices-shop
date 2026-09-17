using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;

namespace MSA.InventoryService.Infrastructure;

/// <summary>
/// Collects read-model keys touched by the current unit of work and evicts them only after commit.
/// Evicting earlier lets a concurrent reader repopulate the cache from pre-commit rows until the TTL expires.
/// </summary>
internal sealed class InventoryCacheInvalidator(
    IDistributedCache cache,
    ILogger<InventoryCacheInvalidator> logger)
{
    private readonly HashSet<string> _pendingKeys = new(StringComparer.Ordinal);

    public void StockChanged(string sku) =>
        _pendingKeys.Add(InventoryCacheKeys.Stock(StockItem.NormalizeSku(sku)));

    public void AllocationChanged(Guid allocationId) =>
        _pendingKeys.Add(InventoryCacheKeys.Allocation(allocationId));

    public void Discard() => _pendingKeys.Clear();

    public async Task FlushAsync()
    {
        var keys = _pendingKeys.ToArray();
        _pendingKeys.Clear();
        foreach (var key in keys)
        {
            try
            {
                // 事务已提交，调用方取消不应阻止缓存修复。
                await cache.RemoveAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Cache invalidation failed for {CacheKey}; the short TTL will repair the read model.", key);
            }
        }
    }
}

/// <summary>Registered outside the transaction behavior so eviction runs after a successful commit.</summary>
internal sealed class InventoryCacheInvalidationBehavior<TRequest, TResponse>(InventoryCacheInvalidator cache)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        TResponse response;
        try
        {
            response = await next();
        }
        catch
        {
            cache.Discard();
            throw;
        }

        await cache.FlushAsync();
        return response;
    }
}
