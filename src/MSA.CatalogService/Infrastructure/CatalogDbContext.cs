using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.CatalogService.Infrastructure;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    IMediator mediator) : MessagingDbContext(options, mediator, "catalog")
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();
    public DbSet<PromotionBenefit> PromotionBenefits => Set<PromotionBenefit>();
    public DbSet<PromotionTarget> PromotionTargets => Set<PromotionTarget>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponQuotaBucket> CouponQuotaBuckets => Set<CouponQuotaBucket>();
    public DbSet<CouponCampaign> CouponCampaigns => Set<CouponCampaign>();
    public DbSet<CouponClaim> CouponClaims => Set<CouponClaim>();
    public DbSet<CatalogAuditEntry> CatalogAuditEntries => Set<CatalogAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 商品名、SKU 的「包含」搜索（后台检索、搜索服务不可用时的前台降级）靠 pg_trgm 的三元组索引。
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
