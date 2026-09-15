using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Application.Abstractions;

public interface ICategoryRepository
{
    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string normalizedCode, Guid? exceptId, CancellationToken cancellationToken);
    Task<bool> HasPublishedProductsAsync(Guid categoryId, CancellationToken cancellationToken);
    void Add(Category category);
}

public interface IProductRepository
{
    Task<Product?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> SkuExistsAsync(string normalizedSku, Guid? exceptId, CancellationToken cancellationToken);
    void Add(Product product);
}

public interface IPromotionRepository
{
    Task<Promotion?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string normalizedCode, Guid? exceptId, CancellationToken cancellationToken);
    void Add(Promotion promotion);
}

public interface ICouponRepository
{
    Task<Coupon?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CodeDigestExistsAsync(string codeDigest, Guid? exceptId, CancellationToken cancellationToken);
    void Add(Coupon coupon);
}

public interface ICatalogReadService
{
    Task<ProductDto?> GetProductAsync(Guid id, bool publicOnly, CancellationToken cancellationToken);
    Task<PagedResult<ProductDto>> ListProductsAsync(
        int skip,
        int take,
        bool publicOnly,
        string? search,
        string? lifecycleStatus,
        Guid? categoryId,
        CancellationToken cancellationToken);
    Task<PagedResult<CategoryDto>> ListCategoriesAsync(
        int skip,
        int take,
        string? status,
        CancellationToken cancellationToken);
    Task<PromotionDto?> GetPromotionAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<PromotionDto>> ListPromotionsAsync(
        int skip,
        int take,
        string? status,
        CancellationToken cancellationToken);
    Task<CouponDto?> GetCouponAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<CouponDto>> ListCouponsAsync(
        int skip,
        int take,
        string? status,
        Guid? promotionId,
        CancellationToken cancellationToken);
}

public interface ICatalogSnapshotPublisher
{
    void Publish(Product product);
    void PublishUnavailable(Product product, ProductVariant variant);
    void Publish(Promotion promotion);
    void Publish(Coupon coupon);
}

public interface ICatalogAuditWriter
{
    void Record(
        string aggregateType,
        Guid aggregateId,
        string operation,
        string actor,
        DateTimeOffset occurredOnUtc,
        object? details = null);
}
