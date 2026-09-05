using Microsoft.Extensions.Caching.Distributed;
using MSA.InventoryService.Application.FlashSales;

namespace MSA.InventoryService.Infrastructure;

internal sealed class InventoryCacheInvalidator(
    IDistributedCache cache,
    ILogger<InventoryCacheInvalidator> logger)
{
    public async Task RemoveStockAsync(string sku, CancellationToken cancellationToken) =>
        await RemoveAsync($"inventory:availability:{sku}", cancellationToken);

    public async Task RemoveFlashSaleAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await RemoveAsync(FlashSaleCacheKeys.Availability(campaignId), cancellationToken);

    private async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cache invalidation failed for {CacheKey}; the short TTL will repair the read model.", key);
        }
    }
}
