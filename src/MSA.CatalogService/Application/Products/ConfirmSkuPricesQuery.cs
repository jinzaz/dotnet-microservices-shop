using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.Infrastrucute.Mediator;
using MSA.Redis;

namespace MSA.CatalogService.Application;

/// <summary>目录对一批 SKU 的权威判定：价格、币种与此刻是否可售。</summary>
public sealed record ConfirmedSkuDto(
    string Sku,
    Guid ProductId,
    Guid VariantId,
    string ProductName,
    string VariantName,
    decimal Price,
    string Currency,
    bool IsActive,
    long Revision,
    Guid CategoryId,
    string AttributesJson);

public sealed record ConfirmedSkuPrices(
    IReadOnlyList<ConfirmedSkuDto> Items,
    IReadOnlyList<string> UnknownSkus);

/// <summary>
/// 供下游（订单）在报价与下单时确认最终价格。投影是最终一致的，改价、下架可能还没同步过去，
/// 这里直接读目录自己的库，是唯一的权威来源。
/// </summary>
public sealed record ConfirmSkuPricesQuery(IReadOnlyList<string> Skus) : IQuery<ConfirmedSkuPrices>;

/// <remarks>
/// 购物车每次打开、订单每次报价与下单都会来问，按 SKU 缓存，商品任何变更提交后逐个失效。
/// 不存在的 SKU 也缓存为空，拿随机 SKU 刷接口不会打穿到数据库。
/// </remarks>
internal sealed class ConfirmSkuPricesQueryHandler(CatalogDbContext db, ICacheStore cache)
    : IRequestHandler<ConfirmSkuPricesQuery, ConfirmedSkuPrices>
{
    public const int MaximumSkus = 200;

    public async Task<ConfirmedSkuPrices> Handle(ConfirmSkuPricesQuery request, CancellationToken cancellationToken)
    {
        var requested = (request.Skus ?? [])
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Select(ProductVariant.NormalizeSku)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0)
            return new ConfirmedSkuPrices([], []);
        if (requested.Length > MaximumSkus)
            throw new ArgumentException($"At most {MaximumSkus} SKUs can be confirmed in one call.", nameof(request));

        var cached = await cache.GetOrLoadManyAsync<ConfirmedSkuDto>(
            requested.Select(CatalogCacheKeys.Price).ToArray(),
            async (missingKeys, token) =>
            {
                var missing = requested.Where(sku => missingKeys.Contains(CatalogCacheKeys.Price(sku))).ToArray();
                var loaded = await LoadAsync(missing, token);
                return loaded.ToDictionary(row => CatalogCacheKeys.Price(ProductVariant.NormalizeSku(row.Sku)), StringComparer.Ordinal);
            },
            CatalogCachePolicies.Price,
            cancellationToken);
        var rows = cached.Values.ToArray();

        var found = rows.Select(row => ProductVariant.NormalizeSku(row.Sku)).ToHashSet(StringComparer.Ordinal);
        return new ConfirmedSkuPrices(rows, requested.Where(sku => !found.Contains(sku)).ToArray());
    }

    private Task<ConfirmedSkuDto[]> LoadAsync(string[] skus, CancellationToken cancellationToken) =>
        db.ProductVariants
            .AsNoTracking()
            .Where(variant => skus.Contains(variant.NormalizedSku))
            .Join(
                db.Products.AsNoTracking(),
                variant => variant.ProductId,
                product => product.Id,
                (variant, product) => new ConfirmedSkuDto(
                    variant.Sku,
                    product.Id,
                    variant.Id,
                    product.Name,
                    variant.DisplayName,
                    variant.Price,
                    variant.Currency,
                    // 可售 = 变体在售且商品已发布；两者缺一不可。
                    variant.IsActive && product.LifecycleStatus == ProductLifecycleStatus.Published,
                    product.Revision,
                    product.CategoryId,
                    variant.AttributesJson))
            .ToArrayAsync(cancellationToken);
}
