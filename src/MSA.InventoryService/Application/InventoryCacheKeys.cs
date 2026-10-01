using MSA.InventoryService.Domain;
using MSA.Redis;

namespace MSA.InventoryService.Application;

/// <summary>
/// 库存服务的缓存键与失效规则放在一起：读的人和让它失效的规则看的是同一份定义。
/// 键按业务对象命名，服务前缀（<c>inventory</c>）由 MSA.Redis 统一加。
/// </summary>
public static class InventoryCacheKeys
{
    public static string Stock(string normalizedSku) => $"availability:{normalizedSku}";

    public static string Allocation(Guid allocationId) => $"allocation:{allocationId:N}";

    /// <summary>某个 SKU 上所有未关闭的限量配额（秒杀）。</summary>
    public static string Offers(string normalizedSku) => $"offers:{normalizedSku}";

    /// <summary>某个 SKU 的配额余量变了 —— 秒杀余量推流订阅的主题。</summary>
    public static string OffersChangedTopic(string normalizedSku) => $"offers-changed:{normalizedSku}";

    /// <summary>
    /// 库存与配额的每一次落库（预占、释放、提交、调整、开关配额）都经过这两条规则，
    /// 提交之后才失效，业务代码里不再需要记得清缓存。
    /// </summary>
    public static void Configure(CacheInvalidationRules rules) => rules
        .On<StockItem>((stock, change) => change.Key(Stock(stock.Sku)))
        .On<InventoryAllocation>((allocation, change) => change
            .Key(Allocation(allocation.Id))
            .Key(Offers(allocation.Sku))
            .Topic(OffersChangedTopic(allocation.Sku)));
}

internal static class InventoryCachePolicies
{
    public static readonly CachePolicy Stock = new(TimeSpan.FromSeconds(15)) { NullExpiration = TimeSpan.FromSeconds(5) };

    // 秒杀期间余量每秒都在变，但每次变都会被提交后失效，TTL 只是兜底。
    public static readonly CachePolicy Offers = new(TimeSpan.FromSeconds(10));
}
