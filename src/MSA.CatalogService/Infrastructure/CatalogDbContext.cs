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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();
    public DbSet<PromotionBenefit> PromotionBenefits => Set<PromotionBenefit>();
    public DbSet<PromotionTarget> PromotionTargets => Set<PromotionTarget>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponQuotaBucket> CouponQuotaBuckets => Set<CouponQuotaBucket>();
    public DbSet<CatalogAuditEntry> CatalogAuditEntries => Set<CatalogAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
