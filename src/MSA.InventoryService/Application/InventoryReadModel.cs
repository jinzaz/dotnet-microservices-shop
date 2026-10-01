using Microsoft.EntityFrameworkCore;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using MSA.Redis;

namespace MSA.InventoryService.Application;

/// <summary>
/// 库存的读模型：商品页查询、下单前的可售预检、秒杀余量推流都从这里读，同一份缓存、同一套失效。
/// 它只用来「看」和「预检」，真正的扣减仍然在预占事务里加锁完成，缓存永远不参与放行。
/// </summary>
internal sealed class InventoryReadModel(InventoryDbContext db, ICacheStore cache, TimeProvider clock)
{
    public Task<InventoryAvailabilityDto?> StockAsync(string normalizedSku, CancellationToken cancellationToken) =>
        cache.GetOrLoadAsync(
            InventoryCacheKeys.Stock(normalizedSku),
            token => db.StockItems
                .AsNoTracking()
                .Where(item => item.Sku == normalizedSku)
                .Select(item => new InventoryAvailabilityDto(
                    item.Sku, item.OnHand, item.Reserved, item.OnHand - item.Reserved, item.Version))
                .SingleOrDefaultAsync(token),
            InventoryCachePolicies.Stock,
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, InventoryAvailabilityDto>> StocksAsync(
        IReadOnlyCollection<string> normalizedSkus,
        CancellationToken cancellationToken)
    {
        var bySku = normalizedSkus.ToDictionary(InventoryCacheKeys.Stock, sku => sku, StringComparer.Ordinal);
        var cached = await cache.GetOrLoadManyAsync<InventoryAvailabilityDto>(
            bySku.Keys,
            async (missingKeys, token) =>
            {
                var skus = missingKeys.Select(key => bySku[key]).ToArray();
                return await db.StockItems
                    .AsNoTracking()
                    .Where(item => skus.Contains(item.Sku))
                    .Select(item => new InventoryAvailabilityDto(
                        item.Sku, item.OnHand, item.Reserved, item.OnHand - item.Reserved, item.Version))
                    .ToDictionaryAsync(item => InventoryCacheKeys.Stock(item.Sku), StringComparer.Ordinal, token);
            },
            InventoryCachePolicies.Stock,
            cancellationToken);
        return cached.Values.ToDictionary(item => item.Sku, StringComparer.Ordinal);
    }

    /// <summary>某个 SKU 上所有未关闭的配额；状态按此刻的时间重新计算（缓存里存的是原始字段）。</summary>
    public async Task<IReadOnlyList<InventoryAllocationDto>> OffersAsync(
        string normalizedSku,
        CancellationToken cancellationToken)
    {
        var offers = await cache.GetOrLoadAsync<IReadOnlyList<InventoryAllocationDto>>(
            InventoryCacheKeys.Offers(normalizedSku),
            async token =>
            {
                var rows = await db.InventoryAllocations
                    .AsNoTracking()
                    .Where(allocation => allocation.Sku == normalizedSku
                                         && allocation.Status == InventoryAllocationStatus.Open)
                    .OrderBy(allocation => allocation.StartsOnUtc)
                    .ToArrayAsync(token);
                var now = clock.GetUtcNow();
                return rows.Select(row => GetInventoryAllocationQueryHandler.ToDto(row, now)).ToArray();
            },
            InventoryCachePolicies.Offers,
            cancellationToken) ?? [];

        var current = clock.GetUtcNow();
        return offers.Select(offer => GetInventoryAllocationQueryHandler.AtTime(offer, current)).ToArray();
    }
}
