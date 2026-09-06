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
        entity.Property(value => value.Code).HasMaxLength(60).IsRequired().HasComment("分类业务编码");
        entity.Property(value => value.NormalizedCode).HasMaxLength(60).IsRequired().HasComment("用于唯一匹配的标准化分类编码");
        entity.Property(value => value.Name).HasMaxLength(120).IsRequired();
        entity.Property(value => value.Status).HasComment("分类生命周期状态");
        entity.HasIndex(value => value.NormalizedCode).IsUnique();
        entity.HasIndex(value => new { value.Status, value.Name, value.Id });
        ConfigureAudit(entity);
    }

    private static void ConfigureAudit(EntityTypeBuilder<Category> entity)
    {
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("分类乐观并发版本号");
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
        entity.Property(value => value.CategoryId).HasComment("商品所属分类标识");
        entity.Property(value => value.Sku).HasMaxLength(80).IsRequired().HasComment("商品 SKU 原始值");
        entity.Property(value => value.NormalizedSku).HasMaxLength(80).IsRequired().HasComment("用于唯一匹配的标准化 SKU");
        entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Description).HasColumnType("text").IsRequired();
        entity.Property(value => value.Price).HasPrecision(18, 2).HasComment("商品当前目录单价");
        entity.Property(value => value.Currency).HasMaxLength(3).IsRequired().HasComment("价格的三位币种代码");
        entity.Property(value => value.LifecycleStatus).HasComment("商品生命周期状态");
        entity.Property(value => value.IsActive).HasComment("面向公共目录查询的可售状态投影");
        entity.Property(value => value.EffectiveOnUtc).HasComment("商品发布生效的 UTC 时间");
        entity.Property(value => value.Revision).HasComment("向下游发布快照时使用的业务修订号");
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("商品乐观并发版本号");
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
        entity.Property(value => value.Code).HasMaxLength(60).IsRequired().HasComment("促销活动业务编码");
        entity.Property(value => value.NormalizedCode).HasMaxLength(60).IsRequired().HasComment("用于唯一匹配的标准化促销编码");
        entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
        entity.Property(value => value.TriggerType).HasComment("促销触发方式：自动或优惠券");
        entity.Property(value => value.Status).HasComment("促销生命周期状态");
        entity.Property(value => value.StartsOnUtc).HasComment("促销窗口开始的 UTC 时间");
        entity.Property(value => value.EndsOnUtc).HasComment("促销窗口结束的 UTC 时间");
        entity.Property(value => value.Priority).HasComment("多促销候选时的计算优先级");
        entity.Property(value => value.StackPolicy).HasComment("促销是否允许与其他优惠叠加");
        entity.Property(value => value.ExclusiveGroup).HasMaxLength(80).HasComment("互斥促销分组标识");
        entity.Property(value => value.PublishedRevision).HasComment("当前已发布定义对应的业务修订号");
        entity.Property(value => value.DefinitionChecksum).HasMaxLength(64).IsRequired().HasComment("已发布促销定义的 SHA-256 校验值");
        entity.Property(value => value.Revision).HasComment("促销定义的业务修订号");
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("促销乐观并发版本号");
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
        entity.Property(value => value.SchemaVersion).HasComment("组件配置结构版本");
        entity.Property(value => value.ConfigurationJson).HasColumnType("jsonb").IsRequired().HasComment("规则、权益或目标的规范化 JSON 配置");
        entity.Property(value => value.Sequence).HasComment("同类组件的稳定计算顺序");
        entity.HasIndex(value => new { value.PromotionId, value.Sequence }).IsUnique();
    }
}

internal sealed class PromotionRuleConfiguration : PromotionComponentConfiguration<PromotionRule>
{
    protected override string TableName => "promotion_rules";

    public override void Configure(EntityTypeBuilder<PromotionRule> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.RuleType).HasMaxLength(80).IsRequired().HasComment("促销适用条件类型");
        entity.HasIndex(value => new { value.RuleType, value.SchemaVersion });
    }
}

internal sealed class PromotionBenefitConfiguration : PromotionComponentConfiguration<PromotionBenefit>
{
    protected override string TableName => "promotion_benefits";

    public override void Configure(EntityTypeBuilder<PromotionBenefit> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.BenefitType).HasMaxLength(80).IsRequired().HasComment("促销优惠权益类型");
        entity.HasIndex(value => new { value.BenefitType, value.SchemaVersion });
    }
}

internal sealed class PromotionTargetConfiguration : PromotionComponentConfiguration<PromotionTarget>
{
    protected override string TableName => "promotion_targets";

    public override void Configure(EntityTypeBuilder<PromotionTarget> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.TargetType).HasMaxLength(40).IsRequired().HasComment("促销目标范围类型");
        entity.Property(value => value.TargetKey).HasMaxLength(200).HasComment("目标商品、SKU 或分类的业务键");
        entity.Property(value => value.IsExcluded).HasComment("是否从促销目标范围中排除");
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
        entity.Property(value => value.PromotionId).HasComment("优惠券关联的促销活动标识");
        entity.Property(value => value.CodeDigest).HasMaxLength(64).IsRequired().HasComment("标准化券码的 SHA-256 摘要，不保存券码明文");
        entity.Property(value => value.MaskedCode).HasMaxLength(124).IsRequired().HasComment("用于展示和审计的脱敏券码");
        entity.Property(value => value.Status).HasComment("优惠券生命周期状态");
        entity.Property(value => value.ValidFromUtc).HasComment("优惠券有效期开始的 UTC 时间");
        entity.Property(value => value.ValidUntilUtc).HasComment("优惠券有效期结束的 UTC 时间");
        entity.Property(value => value.AssignedUserId).HasMaxLength(200).HasComment("专属券绑定的用户标识；为空表示不限定用户");
        entity.Property(value => value.TotalLimit).HasComment("全局可核销总量；为空表示不限制");
        entity.Property(value => value.PerUserLimit).HasComment("单个用户最多可核销次数");
        entity.Property(value => value.QuotaStrategy).HasComment("券配额的单行或分桶并发策略");
        entity.Property(value => value.Revision).HasComment("优惠券定义的业务修订号");
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("优惠券乐观并发版本号");
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
        entity.Property(value => value.BucketNumber).HasComment("用于分散热点写入的配额桶编号");
        entity.Property(value => value.Capacity).HasComment("该配额桶可提供的核销数量");
    }
}

internal sealed class CatalogAuditEntryConfiguration : IEntityTypeConfiguration<CatalogAuditEntry>
{
    public void Configure(EntityTypeBuilder<CatalogAuditEntry> entity)
    {
        entity.ToTable("catalog_audit_entries");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.AggregateType).HasMaxLength(80).IsRequired().HasComment("发生变更的聚合类型");
        entity.Property(value => value.AggregateId).HasComment("发生变更的聚合标识");
        entity.Property(value => value.Operation).HasMaxLength(100).IsRequired().HasComment("业务操作名称");
        entity.Property(value => value.Actor).HasMaxLength(200).IsRequired().HasComment("执行操作的主体标识");
        entity.Property(value => value.OccurredOnUtc).HasComment("业务操作发生的 UTC 时间");
        entity.Property(value => value.DetailsJson).HasColumnType("jsonb").HasComment("操作附加详情的 JSON 快照");
        entity.HasIndex(value => new { value.AggregateType, value.AggregateId, value.OccurredOnUtc });
        entity.HasIndex(value => new { value.Actor, value.OccurredOnUtc });
    }
}
