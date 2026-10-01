using MSA.CatalogService.Domain;
using MSA.Redis;

namespace MSA.CatalogService.Application;

/// <summary>
/// 目录服务的缓存键与失效规则。键按业务对象命名，服务前缀（<c>catalog</c>）由 MSA.Redis 统一加。
/// </summary>
public static class CatalogCacheKeys
{
    /// <summary>前台商品详情（只含公开可售的内容）。</summary>
    public static string Product(Guid productId) => $"product:{productId:N}";

    /// <summary>前台商品列表的命名空间：任何商品变化都整体作废，列表键里带着代际。</summary>
    public const string ProductLists = "product-list";

    /// <summary>某个 SKU 的权威价格与可售判定（订单下单、购物车结算页确认最终价格用）。</summary>
    public static string Price(string normalizedSku) => $"price:{normalizedSku}";

    /// <summary>抢券活动已抢光的标记：热点请求在进数据库行锁之前就被拦下。</summary>
    public static string CampaignSoldOut(Guid campaignId) => $"coupon-campaign:{campaignId:N}:sold-out";

    /// <summary>同一个人在同一场活动上同时只能有一个领取请求在途（连点、脚本并发）。</summary>
    public static string ClaimInFlight(Guid campaignId, string userId) => $"coupon-claim:{campaignId:N}:{userId}";

    /// <summary>
    /// 实体与缓存的对应关系，提交之后才生效。商品的任何部分（主信息、规格、图片、上下架）一变，
    /// 详情、列表和它每个 SKU 的价格确认都失效；活动一有落库（改库存、恢复、领取）就撤掉抢光标记。
    /// </summary>
    public static void Configure(CacheInvalidationRules rules) => rules
        .On<Product>((product, change) =>
        {
            change.Key(Product(product.Id)).Namespace(ProductLists);
            foreach (var variant in product.Variants)
                change.Key(Price(variant.NormalizedSku));
        })
        .On<ProductVariant>((variant, change) => change
            .Key(Product(variant.ProductId))
            .Key(Price(variant.NormalizedSku))
            .Namespace(ProductLists))
        .On<ProductMedia>((media, change) => change
            .Key(Product(media.ProductId))
            .Namespace(ProductLists))
        .On<CouponCampaign>((campaign, change) => change.Key(CampaignSoldOut(campaign.Id)));
}

internal static class CatalogCachePolicies
{
    // 定时上架的商品会在生效时刻之后最多 NullExpiration 才在详情里出现。
    public static readonly CachePolicy ProductDetail = new(TimeSpan.FromMinutes(5))
    {
        NullExpiration = TimeSpan.FromSeconds(15),
        LocalExpiration = TimeSpan.FromSeconds(10)
    };

    public static readonly CachePolicy ProductList = new(TimeSpan.FromSeconds(30))
    {
        LocalExpiration = TimeSpan.FromSeconds(5)
    };

    // 价格是成交依据：不开一级缓存，只靠提交后失效 + 代际校验，旧值可见的时间只有提交到失效之间的毫秒级。
    public static readonly CachePolicy Price = new(TimeSpan.FromMinutes(10))
    {
        NullExpiration = TimeSpan.FromSeconds(30)
    };
}
