using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Infrastructure.Persistence;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("categories", table =>
        {
            table.HasCheckConstraint(
                "CK_categories_status",
                $"\"Status\" BETWEEN {(int)CategoryLifecycleStatus.Draft} AND {(int)CategoryLifecycleStatus.Archived}");
            table.HasCheckConstraint("CK_categories_audit", "\"Version\" > 0 AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            table.HasCheckConstraint("CK_categories_normalized_code", "\"NormalizedCode\" = upper(trim(\"Code\"))");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Code).HasMaxLength(60).IsRequired();
        entity.Property(value => value.NormalizedCode).HasMaxLength(60).IsRequired();
        entity.Property(value => value.Name).HasMaxLength(120).IsRequired();
        entity.HasIndex(value => value.NormalizedCode).IsUnique();
        entity.HasIndex(value => new { value.Status, value.Name, value.Id });
        ConfigureAudit(entity);
    }

    private static void ConfigureAudit(EntityTypeBuilder<Category> entity)
    {
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> entity)
    {
        entity.ToTable("products", table =>
        {
            table.HasCheckConstraint("CK_products_price", "\"Price\" >= 0");
            table.HasCheckConstraint("CK_products_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint(
                "CK_products_lifecycle",
                $"\"LifecycleStatus\" BETWEEN {(int)ProductLifecycleStatus.Draft} AND {(int)ProductLifecycleStatus.Archived}");
            table.HasCheckConstraint("CK_products_revision", "\"Revision\" > 0 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_products_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            table.HasCheckConstraint("CK_products_normalized_sku", "\"NormalizedSku\" = upper(trim(\"Sku\"))");
            table.HasCheckConstraint(
                "CK_products_active_lifecycle",
                $"(\"IsActive\" AND \"LifecycleStatus\" = {(int)ProductLifecycleStatus.Published} AND \"EffectiveOnUtc\" IS NOT NULL) OR (NOT \"IsActive\" AND \"LifecycleStatus\" <> {(int)ProductLifecycleStatus.Published})");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Sku).HasMaxLength(80).IsRequired();
        entity.Property(value => value.NormalizedSku).HasMaxLength(80).IsRequired();
        entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Description).HasColumnType("text").IsRequired();
        entity.Property(value => value.Price).HasPrecision(18, 2);
        entity.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken();
        entity.HasIndex(value => value.Sku).IsUnique();
        entity.HasIndex(value => value.NormalizedSku).IsUnique();
        entity.HasIndex(value => new { value.CategoryId, value.LifecycleStatus, value.Id });
        entity.HasIndex(value => new { value.LifecycleStatus, value.EffectiveOnUtc, value.Id });
        entity.HasOne<Category>()
            .WithMany()
            .HasForeignKey(value => value.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> entity)
    {
        entity.ToTable("promotions", table =>
        {
            table.HasCheckConstraint("CK_promotions_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
            table.HasCheckConstraint("CK_promotions_revision", "\"Revision\" > 0 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_promotions_priority", "\"Priority\" BETWEEN -1000000 AND 1000000");
            table.HasCheckConstraint("CK_promotions_trigger", "\"TriggerType\" IN (0, 1)");
            table.HasCheckConstraint("CK_promotions_status", "\"Status\" BETWEEN 0 AND 5");
            table.HasCheckConstraint("CK_promotions_stack_policy", "\"StackPolicy\" IN (0, 1)");
            table.HasCheckConstraint("CK_promotions_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            table.HasCheckConstraint("CK_promotions_normalized_code", "\"NormalizedCode\" = upper(trim(\"Code\"))");
            table.HasCheckConstraint(
                "CK_promotions_published_snapshot",
                $"\"Status\" = {(int)PromotionStatus.Draft} OR (\"PublishedRevision\" IS NOT NULL AND length(\"DefinitionChecksum\") = 64)");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Code).HasMaxLength(60).IsRequired();
        entity.Property(value => value.NormalizedCode).HasMaxLength(60).IsRequired();
        entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
        entity.Property(value => value.ExclusiveGroup).HasMaxLength(80);
        entity.Property(value => value.DefinitionChecksum).HasMaxLength(64).IsRequired();
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken();
        entity.HasIndex(value => value.NormalizedCode).IsUnique();
        entity.HasIndex(value => new { value.Status, value.StartsOnUtc, value.EndsOnUtc });
        entity.HasMany(value => value.Rules)
            .WithOne()
            .HasForeignKey(value => value.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(value => value.Benefits)
            .WithOne()
            .HasForeignKey(value => value.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(value => value.Targets)
            .WithOne()
            .HasForeignKey(value => value.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(value => value.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.Navigation(value => value.Benefits).UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.Navigation(value => value.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal abstract class PromotionComponentConfiguration<T> : IEntityTypeConfiguration<T>
    where T : class, IPromotionComponent
{
    protected abstract string TableName { get; }

    public virtual void Configure(EntityTypeBuilder<T> entity)
    {
        entity.ToTable(TableName, table =>
        {
            table.HasCheckConstraint($"CK_{TableName}_schema_version", "\"SchemaVersion\" > 0");
            table.HasCheckConstraint($"CK_{TableName}_sequence", "\"Sequence\" >= 0");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ConfigurationJson).HasColumnType("jsonb").IsRequired();
        entity.HasIndex(value => new { value.PromotionId, value.Sequence }).IsUnique();
    }
}

internal sealed class PromotionRuleConfiguration : PromotionComponentConfiguration<PromotionRule>
{
    protected override string TableName => "promotion_rules";

    public override void Configure(EntityTypeBuilder<PromotionRule> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.RuleType).HasMaxLength(80).IsRequired();
        entity.HasIndex(value => new { value.RuleType, value.SchemaVersion });
    }
}

internal sealed class PromotionBenefitConfiguration : PromotionComponentConfiguration<PromotionBenefit>
{
    protected override string TableName => "promotion_benefits";

    public override void Configure(EntityTypeBuilder<PromotionBenefit> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.BenefitType).HasMaxLength(80).IsRequired();
        entity.HasIndex(value => new { value.BenefitType, value.SchemaVersion });
    }
}

internal sealed class PromotionTargetConfiguration : PromotionComponentConfiguration<PromotionTarget>
{
    protected override string TableName => "promotion_targets";

    public override void Configure(EntityTypeBuilder<PromotionTarget> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.TargetType).HasMaxLength(40).IsRequired();
        entity.Property(value => value.TargetKey).HasMaxLength(200);
        entity.HasIndex(value => new { value.TargetType, value.TargetKey, value.IsExcluded });
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> entity)
    {
        entity.ToTable("coupons", table =>
        {
            table.HasCheckConstraint("CK_coupons_window", "\"ValidFromUtc\" < \"ValidUntilUtc\"");
            table.HasCheckConstraint("CK_coupons_total_limit", "\"TotalLimit\" IS NULL OR \"TotalLimit\" > 0");
            table.HasCheckConstraint(
                "CK_coupons_per_user_limit",
                "\"PerUserLimit\" > 0 AND (\"TotalLimit\" IS NULL OR \"PerUserLimit\" <= \"TotalLimit\")");
            table.HasCheckConstraint("CK_coupons_revision", "\"Revision\" > 0 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_coupons_status", "\"Status\" BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_coupons_quota_strategy", "\"QuotaStrategy\" IN (0, 1)");
            table.HasCheckConstraint("CK_coupons_code_digest", "\"CodeDigest\" ~ '^[0-9A-F]{64}$'");
            table.HasCheckConstraint("CK_coupons_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.CodeDigest).HasMaxLength(64).IsRequired();
        entity.Property(value => value.MaskedCode).HasMaxLength(124).IsRequired();
        entity.Property(value => value.AssignedUserId).HasMaxLength(200);
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken();
        entity.HasIndex(value => value.CodeDigest).IsUnique();
        entity.HasIndex(value => new { value.PromotionId, value.Status, value.Id });
        entity.HasIndex(value => new { value.Status, value.ValidUntilUtc, value.Id });
        entity.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(value => value.PromotionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.HasMany(value => value.QuotaBuckets)
            .WithOne()
            .HasForeignKey(value => value.CouponId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(value => value.QuotaBuckets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CouponQuotaBucketConfiguration : IEntityTypeConfiguration<CouponQuotaBucket>
{
    public void Configure(EntityTypeBuilder<CouponQuotaBucket> entity)
    {
        entity.ToTable("coupon_quota_buckets", table =>
        {
            table.HasCheckConstraint("CK_coupon_quota_buckets_number", "\"BucketNumber\" BETWEEN 0 AND 127");
            table.HasCheckConstraint("CK_coupon_quota_buckets_capacity", "\"Capacity\" > 0");
        });
        entity.HasKey(value => new { value.CouponId, value.BucketNumber });
    }
}

internal sealed class CatalogAuditEntryConfiguration : IEntityTypeConfiguration<CatalogAuditEntry>
{
    public void Configure(EntityTypeBuilder<CatalogAuditEntry> entity)
    {
        entity.ToTable("catalog_audit_entries");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.AggregateType).HasMaxLength(80).IsRequired();
        entity.Property(value => value.Operation).HasMaxLength(100).IsRequired();
        entity.Property(value => value.Actor).HasMaxLength(200).IsRequired();
        entity.Property(value => value.DetailsJson).HasColumnType("jsonb");
        entity.HasIndex(value => new { value.AggregateType, value.AggregateId, value.OccurredOnUtc });
        entity.HasIndex(value => new { value.Actor, value.OccurredOnUtc });
    }
}
