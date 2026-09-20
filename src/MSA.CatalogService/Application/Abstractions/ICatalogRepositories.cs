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
    Task<IReadOnlySet<string>> FindExistingDigestsAsync(IReadOnlyCollection<string> codeDigests, CancellationToken cancellationToken);
    void Add(Coupon coupon);
}

public interface ICouponCampaignRepository
{
    /// <summary>Loads the campaign with a row lock; every write (including claims) goes through it, so limits stay exact.</summary>
    Task<CouponCampaign?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<CouponClaimCounts> CountClaimsAsync(Guid campaignId, string userId, DateOnly day, CancellationToken cancellationToken);
    void Add(CouponCampaign campaign);
    void AddClaim(CouponClaim claim);
}

public interface ICouponCampaignReadService
{
    Task<PagedResult<CouponCampaignDto>> ListAsync(int skip, int take, string? status, Guid? promotionId, CancellationToken cancellationToken);
    Task<CouponCampaignDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CouponCampaignStatsDto> GetStatsAsync(Guid id, DateOnly today, int days, CancellationToken cancellationToken);
    Task<PagedResult<CouponClaimDto>> ListClaimsAsync(Guid id, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Open campaigns (listed only, or the one requested) with their promotion and today's/the shopper's counts.</summary>
    Task<IReadOnlyList<StorefrontCouponCampaignDto>> ListStorefrontAsync(
        Guid? campaignId,
        string? userId,
        DateTimeOffset now,
        DateOnly today,
        CancellationToken cancellationToken);
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
        Guid? campaignId,
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

/// <summary>Uniform "load or 404" semantics shared by every catalog command handler.</summary>
public static class CatalogRepositoryExtensions
{
    public static async Task<Category> GetRequiredAsync(
        this ICategoryRepository categories,
        Guid id,
        CancellationToken cancellationToken) =>
        await categories.FindAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("Category was not found.");

    public static async Task<Product> GetRequiredAsync(
        this IProductRepository products,
        Guid id,
        CancellationToken cancellationToken) =>
        await products.FindAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("Product was not found.");

    public static async Task<Promotion> GetRequiredAsync(
        this IPromotionRepository promotions,
        Guid id,
        CancellationToken cancellationToken) =>
        await promotions.FindAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("Promotion was not found.");

    public static async Task<CouponCampaign> GetRequiredAsync(
        this ICouponCampaignRepository campaigns,
        Guid id,
        CancellationToken cancellationToken) =>
        await campaigns.FindForUpdateAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("Coupon campaign was not found.");

    public static async Task<Coupon> GetRequiredAsync(
        this ICouponRepository coupons,
        Guid id,
        CancellationToken cancellationToken) =>
        await coupons.FindAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("Coupon was not found.");
}
