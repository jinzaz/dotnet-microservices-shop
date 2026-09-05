using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Infrastructure;

internal sealed class CatalogStore(CatalogDbContext db) :
    ICategoryRepository,
    IProductRepository,
    IPromotionRepository,
    ICouponRepository,
    ICatalogReadService
{
    Task<Category?> ICategoryRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    Task<bool> ICategoryRepository.ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(category => category.Id == id, cancellationToken);

    Task<bool> ICategoryRepository.CodeExistsAsync(
        string normalizedCode,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(
            category => category.NormalizedCode == normalizedCode
                && (!exceptId.HasValue || category.Id != exceptId.Value),
            cancellationToken);

    Task<bool> ICategoryRepository.HasPublishedProductsAsync(
        Guid categoryId,
        CancellationToken cancellationToken) =>
        db.Products.AnyAsync(
            product => product.CategoryId == categoryId
                && product.LifecycleStatus == ProductLifecycleStatus.Published,
            cancellationToken);

    void ICategoryRepository.Add(Category category) => db.Categories.Add(category);

    Task<Product?> IProductRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

    Task<bool> IProductRepository.SkuExistsAsync(
        string normalizedSku,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        db.Products.AnyAsync(
            product => product.NormalizedSku == normalizedSku
                && (!exceptId.HasValue || product.Id != exceptId.Value),
            cancellationToken);

    void IProductRepository.Add(Product product) => db.Products.Add(product);

    Task<Promotion?> IPromotionRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Promotions
            .Include(promotion => promotion.Rules)
            .Include(promotion => promotion.Benefits)
            .Include(promotion => promotion.Targets)
            .SingleOrDefaultAsync(promotion => promotion.Id == id, cancellationToken);

    Task<bool> IPromotionRepository.CodeExistsAsync(
        string normalizedCode,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        db.Promotions.AnyAsync(
            promotion => promotion.NormalizedCode == normalizedCode
                && (!exceptId.HasValue || promotion.Id != exceptId.Value),
            cancellationToken);

    void IPromotionRepository.Add(Promotion promotion) => db.Promotions.Add(promotion);

    Task<Coupon?> ICouponRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Coupons
            .Include(coupon => coupon.QuotaBuckets)
            .SingleOrDefaultAsync(coupon => coupon.Id == id, cancellationToken);

    Task<bool> ICouponRepository.CodeDigestExistsAsync(
        string codeDigest,
        Guid? exceptId,
        CancellationToken cancellationToken) =>
        db.Coupons.AnyAsync(
            coupon => coupon.CodeDigest == codeDigest
                && (!exceptId.HasValue || coupon.Id != exceptId.Value),
            cancellationToken);

    void ICouponRepository.Add(Coupon coupon) => db.Coupons.Add(coupon);

    async Task<ProductDto?> ICatalogReadService.GetProductAsync(
        Guid id,
        bool publicOnly,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.Products.AsNoTracking().Where(product => product.Id == id);
        if (publicOnly)
        {
            query = query.Where(product =>
                product.LifecycleStatus == ProductLifecycleStatus.Published
                && product.IsActive
                && product.EffectiveOnUtc <= now);
        }

        var product = await query.SingleOrDefaultAsync(cancellationToken);
        return product is null ? null : ToDto(product);
    }

    async Task<PagedResult<ProductDto>> ICatalogReadService.ListProductsAsync(
        int skip,
        int take,
        bool publicOnly,
        string? search,
        string? lifecycleStatus,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var now = DateTimeOffset.UtcNow;
        var query = db.Products.AsNoTracking();
        if (publicOnly)
        {
            query = query.Where(product =>
                product.LifecycleStatus == ProductLifecycleStatus.Published
                && product.IsActive
                && product.EffectiveOnUtc <= now);
        }
        else if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            var status = ParseStatus<ProductLifecycleStatus>(lifecycleStatus);
            query = query.Where(product => product.LifecycleStatus == status);
        }

        if (categoryId.HasValue)
            query = query.Where(product => product.CategoryId == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(product =>
                EF.Functions.ILike(product.Sku, pattern)
                || EF.Functions.ILike(product.Name, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderByDescending(product => product.UpdatedOnUtc)
            .ThenBy(product => product.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<ProductDto>(products.Select(ToDto).ToArray(), skip, take, total);
    }

    async Task<PagedResult<CategoryDto>> ICatalogReadService.ListCategoriesAsync(
        int skip,
        int take,
        string? status,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = db.Categories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseStatus<CategoryLifecycleStatus>(status);
            query = query.Where(category => category.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var categories = await query
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<CategoryDto>(categories.Select(ToDto).ToArray(), skip, take, total);
    }

    async Task<PromotionDto?> ICatalogReadService.GetPromotionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var promotion = await db.Promotions
            .AsNoTracking()
            .Include(value => value.Rules)
            .Include(value => value.Benefits)
            .Include(value => value.Targets)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return promotion is null ? null : ToDto(promotion);
    }

    async Task<PagedResult<PromotionDto>> ICatalogReadService.ListPromotionsAsync(
        int skip,
        int take,
        string? status,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = db.Promotions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseStatus<PromotionStatus>(status);
            query = query.Where(promotion => promotion.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var promotions = await query
            .Include(promotion => promotion.Rules)
            .Include(promotion => promotion.Benefits)
            .Include(promotion => promotion.Targets)
            .AsSplitQuery()
            .OrderByDescending(promotion => promotion.UpdatedOnUtc)
            .ThenBy(promotion => promotion.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<PromotionDto>(promotions.Select(ToDto).ToArray(), skip, take, total);
    }

    async Task<CouponDto?> ICatalogReadService.GetCouponAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var coupon = await db.Coupons
            .AsNoTracking()
            .Include(value => value.QuotaBuckets)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return coupon is null ? null : ToDto(coupon);
    }

    async Task<PagedResult<CouponDto>> ICatalogReadService.ListCouponsAsync(
        int skip,
        int take,
        string? status,
        Guid? promotionId,
        CancellationToken cancellationToken)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = db.Coupons.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseStatus<CouponStatus>(status);
            query = query.Where(coupon => coupon.Status == parsed);
        }
        if (promotionId.HasValue)
            query = query.Where(coupon => coupon.PromotionId == promotionId.Value);

        var total = await query.CountAsync(cancellationToken);
        var coupons = await query
            .Include(coupon => coupon.QuotaBuckets)
            .OrderByDescending(coupon => coupon.UpdatedOnUtc)
            .ThenBy(coupon => coupon.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedResult<CouponDto>(coupons.Select(ToDto).ToArray(), skip, take, total);
    }

    private static ProductDto ToDto(Product product) => new(
        product.Id,
        product.CategoryId,
        product.Sku,
        product.Name,
        product.Description,
        product.Price,
        product.Currency,
        product.IsActive,
        product.UpdatedOnUtc,
        product.LifecycleStatus.ToString(),
        product.NormalizedSku,
        product.EffectiveOnUtc,
        product.Revision,
        product.Version);

    private static CategoryDto ToDto(Category category) => new(
        category.Id,
        category.Code,
        category.Name,
        category.Status.ToString(),
        category.CreatedOnUtc,
        category.UpdatedOnUtc,
        category.CreatedBy,
        category.UpdatedBy,
        category.Version);

    private static PromotionDto ToDto(Promotion promotion) => new(
        promotion.Id,
        promotion.Code,
        promotion.Name,
        promotion.TriggerType.ToString(),
        promotion.Status.ToString(),
        promotion.StartsOnUtc,
        promotion.EndsOnUtc,
        promotion.Priority,
        promotion.StackPolicy.ToString(),
        promotion.ExclusiveGroup,
        promotion.Revision,
        promotion.DefinitionChecksum,
        promotion.Rules.OrderBy(rule => rule.Sequence).Select(rule => new PromotionRuleDto(
            rule.Id, rule.RuleType, rule.SchemaVersion, rule.ConfigurationJson, rule.Sequence)).ToArray(),
        promotion.Benefits.OrderBy(benefit => benefit.Sequence).Select(benefit => new PromotionBenefitDto(
            benefit.Id, benefit.BenefitType, benefit.SchemaVersion, benefit.ConfigurationJson, benefit.Sequence)).ToArray(),
        promotion.Targets.OrderBy(target => target.Sequence).Select(target => new PromotionTargetDto(
            target.Id, target.TargetType, target.TargetKey, target.IsExcluded)).ToArray(),
        promotion.CreatedOnUtc,
        promotion.UpdatedOnUtc,
        promotion.CreatedBy,
        promotion.UpdatedBy,
        promotion.Version);

    private static CouponDto ToDto(Coupon coupon) => new(
        coupon.Id,
        coupon.PromotionId,
        coupon.MaskedCode,
        coupon.Status.ToString(),
        coupon.ValidFromUtc,
        coupon.ValidUntilUtc,
        coupon.AssignedUserId,
        coupon.TotalLimit,
        coupon.PerUserLimit,
        coupon.QuotaStrategy.ToString(),
        coupon.Revision,
        coupon.QuotaBuckets.OrderBy(bucket => bucket.BucketNumber).Select(bucket =>
            new CouponQuotaBucketDto(bucket.BucketNumber, bucket.Capacity)).ToArray(),
        coupon.CreatedOnUtc,
        coupon.UpdatedOnUtc,
        coupon.CreatedBy,
        coupon.UpdatedBy,
        coupon.Version);

    private static T ParseStatus<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown {typeof(T).Name} value '{value}'.", nameof(value));
}
