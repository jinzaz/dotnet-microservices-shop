using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record SearchFeedVariantDto(
    string Sku,
    string NormalizedSku,
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    string Currency,
    bool IsDefault);

/// <summary>搜索服务建索引文档所需的商品快照。是否「可搜索」由目录判定，搜索服务不重复这条业务规则。</summary>
public sealed record SearchFeedProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Description,
    long Version,
    bool Searchable,
    DateTimeOffset? EffectiveOnUtc,
    DateTimeOffset UpdatedOnUtc,
    IReadOnlyList<SearchFeedVariantDto> Variants,
    string? ImageUrl,
    Guid? BrandId = null,
    string? BrandName = null);

public sealed record SearchFeedPage(IReadOnlyList<SearchFeedProductDto> Products, Guid? NextAfterId);

/// <summary>按 id 取最新快照（增量同步）。已删除的商品不在结果里，调用方据此删文档。</summary>
public sealed record GetSearchFeedProductsQuery(IReadOnlyList<Guid> ProductIds) : IQuery<IReadOnlyList<SearchFeedProductDto>>;

/// <summary>按主键分页导出可搜索的商品（全量重建、分类 / 品牌改名后刷新）。</summary>
public sealed record ExportSearchFeedProductsQuery(Guid? AfterId, int PageSize, Guid? CategoryId, Guid? BrandId = null) : IQuery<SearchFeedPage>;

internal sealed class GetSearchFeedProductsHandler(CatalogDbContext db)
    : IRequestHandler<GetSearchFeedProductsQuery, IReadOnlyList<SearchFeedProductDto>>
{
    public const int MaximumProducts = 1_000;

    public async Task<IReadOnlyList<SearchFeedProductDto>> Handle(GetSearchFeedProductsQuery request, CancellationToken cancellationToken)
    {
        var ids = request.ProductIds.Distinct().ToArray();
        if (ids.Length > MaximumProducts)
            throw new ArgumentException($"At most {MaximumProducts} products can be read in one call.", nameof(request));
        return ids.Length == 0 ? [] : await SearchFeed.LoadAsync(db, ids, cancellationToken);
    }
}

internal sealed class ExportSearchFeedProductsHandler(CatalogDbContext db)
    : IRequestHandler<ExportSearchFeedProductsQuery, SearchFeedPage>
{
    public async Task<SearchFeedPage> Handle(ExportSearchFeedProductsQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, GetSearchFeedProductsHandler.MaximumProducts);
        var after = request.AfterId ?? Guid.Empty;
        // 按主键 keyset 分页：翻到第 50 万行也只是一次索引范围扫描；OFFSET 则要先数过前面所有行。
        var published = (int)ProductLifecycleStatus.Published;
        var ids = request.CategoryId is { } categoryId
            ? await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM catalog_products
                WHERE "Id" > {after} AND "LifecycleStatus" = {published} AND "IsActive" AND "CategoryId" = {categoryId}
                ORDER BY "Id" LIMIT {pageSize}
                """).ToArrayAsync(cancellationToken)
            : request.BrandId is { } brandId
            ? await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM catalog_products
                WHERE "Id" > {after} AND "LifecycleStatus" = {published} AND "IsActive" AND "BrandId" = {brandId}
                ORDER BY "Id" LIMIT {pageSize}
                """).ToArrayAsync(cancellationToken)
            : await db.Database.SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM catalog_products
                WHERE "Id" > {after} AND "LifecycleStatus" = {published} AND "IsActive"
                ORDER BY "Id" LIMIT {pageSize}
                """).ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
            return new SearchFeedPage([], null);

        var products = await SearchFeed.LoadAsync(db, ids, cancellationToken);
        return new SearchFeedPage(
            products.Where(product => product.Searchable).ToArray(),
            ids.Length == pageSize ? ids[^1] : null);
    }
}

internal static class SearchFeed
{
    public static async Task<IReadOnlyList<SearchFeedProductDto>> LoadAsync(
        CatalogDbContext db,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .Include(product => product.Variants)
            .Include(product => product.Media)
            .AsSplitQuery()
            .ToArrayAsync(cancellationToken);
        var categoryIds = products.Select(product => product.CategoryId).Distinct().ToArray();
        var categoryNames = await db.Categories
            .AsNoTracking()
            .Where(category => categoryIds.Contains(category.Id))
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);
        var brandIds = products.Select(product => product.BrandId).OfType<Guid>().Distinct().ToArray();
        var brandNames = brandIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Brands
                .AsNoTracking()
                .Where(brand => brandIds.Contains(brand.Id))
                .ToDictionaryAsync(brand => brand.Id, brand => brand.Name, cancellationToken);
        return products
            .OrderBy(product => product.Id)
            .Select(product => ToDto(
                product,
                categoryNames.GetValueOrDefault(product.CategoryId, string.Empty),
                product.BrandId is { } brandId ? brandNames.GetValueOrDefault(brandId) : null))
            .ToArray();
    }

    /// <summary>可见性规则与前台列表 / 详情一致（见 CatalogStore）：已发布、可售、至少一个可售规格；图片只取通用图或可售规格的图。</summary>
    private static SearchFeedProductDto ToDto(Product product, string categoryName, string? brandName)
    {
        var variants = product.Variants
            .Where(variant => variant.IsActive)
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.DisplayName, StringComparer.Ordinal)
            .ThenBy(variant => variant.Id)
            .ToArray();
        var image = product.Media
            .Where(media => media.MediaType == ProductMediaType.Image)
            .Where(media => media.VariantId is null || variants.Any(variant => variant.Id == media.VariantId))
            .OrderBy(media => media.SortOrder)
            .ThenBy(media => media.Id)
            .Select(media => media.Url)
            .FirstOrDefault();
        var searchable = product is { LifecycleStatus: ProductLifecycleStatus.Published, IsActive: true, EffectiveOnUtc: not null }
                         && variants.Length > 0;

        return new SearchFeedProductDto(
            product.Id,
            product.CategoryId,
            categoryName,
            product.Name,
            product.Description,
            product.Version,
            searchable,
            product.EffectiveOnUtc,
            product.UpdatedOnUtc,
            variants.Select(variant => new SearchFeedVariantDto(
                variant.Sku,
                variant.NormalizedSku,
                variant.DisplayName,
                variant.AttributesJson,
                variant.Barcode,
                variant.Price,
                variant.Currency,
                variant.IsDefault)).ToArray(),
            image,
            product.BrandId,
            brandName);
    }
}
