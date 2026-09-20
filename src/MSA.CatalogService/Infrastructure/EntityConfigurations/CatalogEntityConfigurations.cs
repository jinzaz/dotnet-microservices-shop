using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Infrastructure.EntityConfigurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("catalog_categories", table =>
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
        entity.ToTable("catalog_products", table =>
        {
            table.HasCheckConstraint(
                "CK_products_lifecycle",
                $"\"LifecycleStatus\" BETWEEN {(int)ProductLifecycleStatus.Draft} AND {(int)ProductLifecycleStatus.Archived}");
            table.HasCheckConstraint("CK_products_revision", "\"Revision\" > 0 AND \"Version\" > 0");
            table.HasCheckConstraint("CK_products_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            table.HasCheckConstraint(
                "CK_products_active_lifecycle",
                $"(\"IsActive\" AND \"LifecycleStatus\" = {(int)ProductLifecycleStatus.Published} AND \"EffectiveOnUtc\" IS NOT NULL) OR (NOT \"IsActive\" AND \"LifecycleStatus\" <> {(int)ProductLifecycleStatus.Published})");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.CategoryId).HasComment("商品所属分类标识");
        entity.Property(value => value.Name).HasMaxLength(200).IsRequired().HasComment("SPU 商品名称");
        entity.Property(value => value.Description).HasColumnType("text").IsRequired().HasComment("SPU 商品描述");
        entity.Property(value => value.LifecycleStatus).HasComment("商品生命周期状态");
        entity.Property(value => value.IsActive).HasComment("面向公共目录查询的可售状态");
        entity.Property(value => value.EffectiveOnUtc).HasComment("商品发布生效的 UTC 时间");
        entity.Property(value => value.Revision).HasComment("向下游发布商品快照时使用的业务修订号");
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("商品乐观并发版本号");
        entity.Ignore(value => value.DefaultVariant);
        entity.Ignore(value => value.Sku);
        entity.Ignore(value => value.NormalizedSku);
        entity.Ignore(value => value.Price);
        entity.Ignore(value => value.Currency);
        entity.HasIndex(value => new { value.CategoryId, value.LifecycleStatus, value.Id });
        entity.HasIndex(value => new { value.LifecycleStatus, value.EffectiveOnUtc, value.Id });
        entity.HasOne<Category>()
            .WithMany()
            .HasForeignKey(value => value.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        entity.HasMany(value => value.Variants)
            .WithOne()
            .HasForeignKey(value => value.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(value => value.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasMany(value => value.Media)
            .WithOne()
            .HasForeignKey(value => value.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(value => value.Media).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> entity)
    {
        entity.ToTable("catalog_product_variants", table =>
        {
            table.HasCheckConstraint("CK_product_variants_price", "\"Price\" >= 0");
            table.HasCheckConstraint("CK_product_variants_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_product_variants_normalized_sku", "\"NormalizedSku\" = upper(trim(\"Sku\"))");
            table.HasCheckConstraint("CK_product_variants_attributes", "jsonb_typeof(\"AttributesJson\") = 'object'");
            table.HasCheckConstraint("CK_product_variants_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ProductId).HasComment("SKU 所属的 SPU 商品标识");
        entity.Property(value => value.Sku).HasMaxLength(80).IsRequired().HasComment("SKU 原始值");
        entity.Property(value => value.NormalizedSku).HasMaxLength(80).IsRequired().HasComment("用于唯一匹配的标准化 SKU");
        entity.Property(value => value.DisplayName).HasMaxLength(200).IsRequired().HasComment("SKU 展示名称");
        entity.Property(value => value.AttributesJson).HasColumnType("jsonb").IsRequired().HasComment("颜色、尺码等销售属性 JSON 对象");
        entity.Property(value => value.Barcode).HasMaxLength(80).HasComment("SKU 条码");
        entity.Property(value => value.Price).HasPrecision(18, 2).HasComment("SKU 当前目录单价");
        entity.Property(value => value.Currency).HasMaxLength(3).IsRequired().HasComment("价格的三位币种代码");
        entity.Property(value => value.IsActive).HasComment("SKU 是否启用销售");
        entity.Property(value => value.IsDefault).HasComment("是否为兼容单 SKU 接口的默认 SKU");
        entity.Property(value => value.CreatedOnUtc).HasComment("SKU 创建的 UTC 时间");
        entity.Property(value => value.UpdatedOnUtc).HasComment("SKU 最近更新的 UTC 时间");
        entity.HasIndex(value => value.NormalizedSku).IsUnique();
        entity.HasIndex(value => value.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
        entity.HasIndex(value => new { value.ProductId, value.IsDefault })
            .IsUnique()
            .HasFilter("\"IsDefault\"");
        entity.HasIndex(value => new { value.ProductId, value.IsActive, value.Id });
    }
}

internal sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> entity)
    {
        entity.ToTable("catalog_product_media", table =>
        {
            table.HasCheckConstraint("CK_product_media_type", "\"MediaType\" BETWEEN 0 AND 1");
            table.HasCheckConstraint("CK_product_media_sort_order", "\"SortOrder\" BETWEEN 0 AND 10000");
            table.HasCheckConstraint("CK_product_media_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ProductId).HasComment("媒体所属的 SPU 商品标识");
        entity.Property(value => value.VariantId).HasComment("媒体限定的 SKU 标识；空值表示 SPU 通用媒体");
        entity.Property(value => value.MediaType).HasComment("媒体类型：图片或视频");
        entity.Property(value => value.Url).HasMaxLength(2_000).IsRequired().HasComment("媒体资源 URL");
        entity.Property(value => value.ThumbnailUrl).HasMaxLength(2_000).HasComment("视频或大图的缩略图 URL");
        entity.Property(value => value.AltText).HasMaxLength(300).HasComment("图片替代文本或视频说明");
        entity.Property(value => value.SortOrder).HasComment("同一商品内的展示顺序");
        entity.Property(value => value.CreatedOnUtc).HasComment("媒体创建的 UTC 时间");
        entity.Property(value => value.UpdatedOnUtc).HasComment("媒体最近更新的 UTC 时间");
        entity.HasIndex(value => new { value.ProductId, value.VariantId, value.SortOrder, value.Id });
        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(value => value.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> entity)
    {
        entity.ToTable("catalog_promotions", table =>
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
    protected override string TableName => "catalog_promotion_rules";

    public override void Configure(EntityTypeBuilder<PromotionRule> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.RuleType).HasMaxLength(80).IsRequired().HasComment("促销适用条件类型");
        entity.HasIndex(value => new { value.RuleType, value.SchemaVersion });
    }
}

internal sealed class PromotionBenefitConfiguration : PromotionComponentConfiguration<PromotionBenefit>
{
    protected override string TableName => "catalog_promotion_benefits";

    public override void Configure(EntityTypeBuilder<PromotionBenefit> entity)
    {
        base.Configure(entity);
        entity.Property(value => value.BenefitType).HasMaxLength(80).IsRequired().HasComment("促销优惠权益类型");
        entity.HasIndex(value => new { value.BenefitType, value.SchemaVersion });
    }
}

internal sealed class PromotionTargetConfiguration : PromotionComponentConfiguration<PromotionTarget>
{
    protected override string TableName => "catalog_promotion_targets";

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
        entity.ToTable("catalog_coupons", table =>
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
        entity.Property(value => value.CampaignId).HasComment("领券活动标识；为空表示后台直接创建或批量发放");
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
        entity.HasIndex(value => value.CampaignId);
        entity.HasOne<CouponCampaign>()
            .WithMany()
            .HasForeignKey(value => value.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
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

internal sealed class CouponCampaignConfiguration : IEntityTypeConfiguration<CouponCampaign>
{
    public void Configure(EntityTypeBuilder<CouponCampaign> entity)
    {
        entity.ToTable("catalog_coupon_campaigns", table =>
        {
            table.HasCheckConstraint("CK_coupon_campaigns_window", "\"ClaimStartsOnUtc\" < \"ClaimEndsOnUtc\"");
            table.HasCheckConstraint("CK_coupon_campaigns_status", "\"Status\" BETWEEN 0 AND 3");
            // 与领域校验互为兜底：即使绕过聚合直接改库，也不会出现超发。
            table.HasCheckConstraint("CK_coupon_campaigns_claimed", "\"ClaimedQuantity\" >= 0 AND (\"TotalQuantity\" IS NULL OR \"ClaimedQuantity\" <= \"TotalQuantity\")");
            table.HasCheckConstraint("CK_coupon_campaigns_limits", "\"PerUserLimit\" > 0 AND \"UsesPerCoupon\" > 0 AND (\"DailyQuantity\" IS NULL OR \"DailyQuantity\" > 0) AND (\"PerUserDailyLimit\" IS NULL OR \"PerUserDailyLimit\" BETWEEN 1 AND \"PerUserLimit\")");
            table.HasCheckConstraint("CK_coupon_campaigns_validity", "(\"ValidityMode\" = 0 AND \"ValidFromUtc\" < \"ValidUntilUtc\" AND \"ValidDays\" IS NULL) OR (\"ValidityMode\" = 1 AND \"ValidDays\" > 0 AND \"ValidFromUtc\" IS NULL AND \"ValidUntilUtc\" IS NULL)");
            table.HasCheckConstraint("CK_coupon_campaigns_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.PromotionId).HasComment("领券活动所属的凭券促销标识");
        entity.Property(value => value.Title).HasMaxLength(60).IsRequired().HasComment("领券中心展示的标题");
        entity.Property(value => value.Description).HasMaxLength(200).HasComment("领券中心展示的补充说明");
        entity.Property(value => value.Status).HasComment("领券活动状态");
        entity.Property(value => value.ClaimStartsOnUtc).HasComment("开始领取的 UTC 时间");
        entity.Property(value => value.ClaimEndsOnUtc).HasComment("停止领取的 UTC 时间");
        entity.Property(value => value.TotalQuantity).HasComment("可领取总量；为空表示不限量");
        entity.Property(value => value.ClaimedQuantity).HasComment("已领取数量，在活动行锁内递增");
        entity.Property(value => value.DailyQuantity).HasComment("每日可领取总量；为空表示不限");
        entity.Property(value => value.PerUserLimit).HasComment("每个用户最多可领取张数");
        entity.Property(value => value.PerUserDailyLimit).HasComment("每个用户每日最多可领取张数；为空表示不限");
        entity.Property(value => value.ValidityMode).HasComment("券有效期模式：固定时段或领取后 N 天");
        entity.Property(value => value.ValidFromUtc).HasComment("固定时段模式的券生效 UTC 时间");
        entity.Property(value => value.ValidUntilUtc).HasComment("固定时段模式的券失效 UTC 时间");
        entity.Property(value => value.ValidDays).HasComment("领取后有效天数");
        entity.Property(value => value.UsesPerCoupon).HasComment("每张领取的券可使用次数");
        entity.Property(value => value.IsListed).HasComment("是否在领券中心展示；否则仅凭链接领取");
        entity.Property(value => value.CreatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.UpdatedBy).HasMaxLength(200).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("领券活动乐观并发版本号");
        entity.Ignore(value => value.RemainingQuantity);
        entity.HasIndex(value => new { value.Status, value.ClaimEndsOnUtc, value.Id });
        entity.HasIndex(value => new { value.PromotionId, value.Status });
        entity.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(value => value.PromotionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}

internal sealed class CouponClaimConfiguration : IEntityTypeConfiguration<CouponClaim>
{
    public void Configure(EntityTypeBuilder<CouponClaim> entity)
    {
        entity.ToTable("catalog_coupon_claims");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.CampaignId).HasComment("领券活动标识");
        entity.Property(value => value.UserId).HasMaxLength(200).IsRequired().HasComment("领取用户标识");
        entity.Property(value => value.CouponId).HasComment("领取后生成的专属券标识");
        entity.Property(value => value.ClaimedOnUtc).HasComment("领取的 UTC 时间");
        entity.Property(value => value.ClaimDay).HasComment("领取所在的业务日，用于每日限额");
        entity.HasIndex(value => new { value.CampaignId, value.UserId, value.ClaimDay });
        entity.HasIndex(value => new { value.CampaignId, value.ClaimDay });
        entity.HasIndex(value => new { value.CampaignId, value.ClaimedOnUtc });
        entity.HasIndex(value => value.CouponId).IsUnique();
        entity.HasOne<CouponCampaign>()
            .WithMany()
            .HasForeignKey(value => value.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Coupon>()
            .WithMany()
            .HasForeignKey(value => value.CouponId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CouponQuotaBucketConfiguration : IEntityTypeConfiguration<CouponQuotaBucket>
{
    public void Configure(EntityTypeBuilder<CouponQuotaBucket> entity)
    {
        entity.ToTable("catalog_coupon_quota_buckets", table =>
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
