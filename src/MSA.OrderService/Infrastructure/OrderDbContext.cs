using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.OrderService.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderFulfillmentStatusHistory> OrderFulfillmentStatusHistory => Set<OrderFulfillmentStatusHistory>();
    public DbSet<OrderProductProjection> ProductProjections => Set<OrderProductProjection>();
    public DbSet<FlashSaleCampaignProjection> FlashSaleCampaignProjections => Set<FlashSaleCampaignProjection>();
    public DbSet<PromotionProjection> PromotionProjections => Set<PromotionProjection>();
    public DbSet<CouponProjection> CouponProjections => Set<CouponProjection>();
    public DbSet<CouponQuotaBucket> CouponQuotaBuckets => Set<CouponQuotaBucket>();
    public DbSet<OrderCouponRedemption> CouponRedemptions => Set<OrderCouponRedemption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureCatalogProjections(modelBuilder);
        ConfigurePromotionProjections(modelBuilder);
    }

    private static void ConfigureOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders", table =>
            {
                table.HasCheckConstraint("CK_orders_amounts", "\"SubtotalAmount\" > 0 AND \"DiscountAmount\" >= 0 AND \"Amount\" > 0 AND \"Amount\" = \"SubtotalAmount\" - \"DiscountAmount\"");
                table.HasCheckConstraint("CK_orders_status", "\"Status\" BETWEEN 0 AND 3");
                table.HasCheckConstraint("CK_orders_payment_due", "\"PaymentDueOnUtc\" > \"CreatedOnUtc\"");
                table.HasCheckConstraint("CK_orders_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("CK_orders_payment_method", "\"PaymentMethod\" IN ('Alipay', 'WeChatPay', 'Unknown')");
                table.HasCheckConstraint("CK_orders_snapshot_hashes", "\"RequestFingerprint\" ~ '^[0-9A-F]{64}$' AND \"PricingSnapshotHash\" ~ '^[0-9A-F]{64}$'");
                table.HasCheckConstraint("CK_orders_confirmed_fields", "\"Status\" <> 2 OR (\"ReservationId\" IS NOT NULL AND \"PaymentId\" IS NOT NULL AND \"ConfirmedOnUtc\" IS NOT NULL)");
                table.HasCheckConstraint("CK_orders_cancelled_fields", "\"Status\" <> 3 OR (\"CancellationReason\" IS NOT NULL AND \"CancelledOnUtc\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_orders_source_basket_pair",
                    "(\"SourceBasketId\" IS NULL AND \"SourceBasketVersion\" IS NULL) OR (\"SourceBasketId\" IS NOT NULL AND \"SourceBasketVersion\" IS NOT NULL AND \"SourceBasketVersion\" >= 0)");
            });
            entity.HasKey(order => order.Id);
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasIndex(order => new { order.UserId, order.IdempotencyKey }).IsUnique();
            entity.HasIndex(order => new
                {
                    order.UserId,
                    order.SourceBasketId,
                    order.SourceBasketVersion
                })
                .IsUnique()
                .HasFilter("\"SourceBasketId\" IS NOT NULL AND \"SourceBasketVersion\" IS NOT NULL");
            entity.HasIndex(order => new { order.UserId, order.CreatedOnUtc });
            entity.HasIndex(order => new { order.Status, order.CreatedOnUtc });
            entity.HasIndex(order => new { order.FulfillmentStatus, order.UpdatedOnUtc });
            entity.HasIndex(order => new { order.FlashSaleCampaignId, order.Status });
            entity.HasIndex(order => new { order.FlashSaleCampaignId, order.UserId })
                .IsUnique()
                .HasFilter("\"FlashSaleCampaignId\" IS NOT NULL");
            entity.Property(order => order.OrderNumber).HasMaxLength(48).HasComment("面向用户和运营的订单编号");
            entity.Property(order => order.UserId).HasMaxLength(200).HasComment("下单用户标识");
            entity.Property(order => order.IdempotencyKey).HasMaxLength(100).HasComment("用户维度的下单幂等键");
            entity.Property(order => order.RequestFingerprint).HasMaxLength(64).HasComment("原始下单请求的 SHA-256 指纹");
            entity.Property(order => order.PaymentMethod).HasMaxLength(40).HasComment("用户选择的支付方式");
            entity.Property(order => order.SourceBasketId).HasComment("发起结算的购物车标识");
            entity.Property(order => order.SourceBasketVersion).HasComment("发起结算的购物车版本，用于结算幂等");
            entity.Property(order => order.SubtotalAmount).HasPrecision(18, 2).HasComment("优惠前商品小计");
            entity.Property(order => order.DiscountAmount).HasPrecision(18, 2).HasComment("订单优惠总额");
            entity.Property(order => order.Amount).HasPrecision(18, 2).HasComment("最终应付金额");
            entity.Property(order => order.Currency).HasMaxLength(3).HasComment("订单金额的三位币种代码");
            entity.Property(order => order.PricingSnapshotId).HasComment("本次定价快照标识");
            entity.Property(order => order.PricingSnapshotHash).HasMaxLength(64).HasComment("商品与优惠定价快照的 SHA-256 摘要");
            entity.Property(order => order.PaymentDueOnUtc).HasComment("订单支付截止的 UTC 时间");
            entity.Property(order => order.Status).HasComment("订单交易状态");
            entity.Property(order => order.FulfillmentStatus).HasComment("订单履约状态");
            entity.Property(order => order.ReservationId).HasComment("库存服务返回的预占标识");
            entity.Property(order => order.PaymentId).HasComment("支付服务返回的支付单标识");
            entity.Property(order => order.CancellationReason).HasMaxLength(1000).HasComment("订单取消原因");
            entity.Property(order => order.CarrierCode).HasMaxLength(50);
            entity.Property(order => order.CarrierName).HasMaxLength(120);
            entity.Property(order => order.TrackingNumber).HasMaxLength(120);
            entity.Property(order => order.Version).IsConcurrencyToken().HasComment("订单乐观并发版本号");

            entity.OwnsOne(order => order.ShippingAddress, address =>
            {
                address.Property(value => value.SourceAddressId).HasColumnName("ShippingAddressId").HasComment("用户地址簿中的来源地址标识");
                address.Property(value => value.RecipientName).HasColumnName("ShippingRecipientName").HasMaxLength(100);
                address.Property(value => value.PhoneNumber).HasColumnName("ShippingPhoneNumber").HasMaxLength(32);
                address.Property(value => value.CountryCode).HasColumnName("ShippingCountryCode").HasMaxLength(3);
                address.Property(value => value.Province).HasColumnName("ShippingProvince").HasMaxLength(100);
                address.Property(value => value.City).HasColumnName("ShippingCity").HasMaxLength(100);
                address.Property(value => value.District).HasColumnName("ShippingDistrict").HasMaxLength(100);
                address.Property(value => value.StreetLine1).HasColumnName("ShippingStreetLine1").HasMaxLength(300);
                address.Property(value => value.StreetLine2).HasColumnName("ShippingStreetLine2").HasMaxLength(300);
                address.Property(value => value.PostalCode).HasColumnName("ShippingPostalCode").HasMaxLength(20);
            });
            entity.Navigation(order => order.ShippingAddress).IsRequired();

            entity.HasMany(order => order.Lines)
                .WithOne()
                .HasForeignKey("OrderId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(order => order.Discounts)
                .WithOne()
                .HasForeignKey("OrderId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(order => order.Discounts).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(order => order.StatusHistory)
                .WithOne()
                .HasForeignKey(history => history.OrderId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(order => order.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(order => order.FulfillmentStatusHistory)
                .WithOne()
                .HasForeignKey(history => history.OrderId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(order => order.FulfillmentStatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("order_lines", table =>
            {
                table.HasCheckConstraint("CK_order_lines_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_order_lines_prices", "\"ListUnitPrice\" >= 0 AND \"UnitPrice\" >= 0 AND \"UnitPrice\" <= \"ListUnitPrice\" AND \"LineSubtotal\" = round(\"ListUnitPrice\" * \"Quantity\", 2) AND \"DiscountAmount\" = round((\"ListUnitPrice\" - \"UnitPrice\") * \"Quantity\", 2) AND \"LineAmount\" = round(\"UnitPrice\" * \"Quantity\", 2)");
            });
            entity.HasKey(line => line.Id);
            entity.HasIndex("OrderId", nameof(OrderLine.Sku)).IsUnique();
            entity.Property(line => line.Sku).HasMaxLength(80);
            entity.Property(line => line.ProductName).HasMaxLength(200);
            entity.Property(line => line.Quantity).HasComment("下单商品数量");
            entity.Property(line => line.ListUnitPrice).HasPrecision(18, 2).HasComment("下单时的目录单价快照");
            entity.Property(line => line.UnitPrice).HasPrecision(18, 2).HasComment("分摊优惠后的成交单价");
            entity.Property(line => line.LineSubtotal).HasPrecision(18, 2).HasComment("优惠前行小计");
            entity.Property(line => line.DiscountAmount).HasPrecision(18, 2).HasComment("订单行优惠金额");
            entity.Property(line => line.LineAmount).HasPrecision(18, 2).HasComment("订单行最终应付金额");
        });

        modelBuilder.Entity<OrderDiscount>(entity =>
        {
            entity.ToTable("order_discounts", table =>
                table.HasCheckConstraint("CK_order_discounts_amount", "\"Amount\" > 0"));
            entity.HasKey(discount => discount.Id);
            entity.HasIndex("OrderId", nameof(OrderDiscount.PromotionId), nameof(OrderDiscount.CouponId));
            entity.Property(discount => discount.SourceType).HasMaxLength(40);
            entity.Property(discount => discount.Description).HasMaxLength(300);
            entity.Property(discount => discount.PromotionRevision).HasComment("计算优惠时采用的促销修订号");
            entity.Property(discount => discount.CouponId).HasComment("优惠来源券标识；自动促销时为空");
            entity.Property(discount => discount.Amount).HasPrecision(18, 2).HasComment("该优惠在订单上的抵扣金额");
            entity.Property(discount => discount.SnapshotJson).HasColumnType("jsonb").HasComment("用于审计和复算的促销定义快照");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("order_status_history", table =>
                table.HasCheckConstraint("CK_order_status_history_status", "(\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 0 AND 3) AND \"ToStatus\" BETWEEN 0 AND 3"));
            entity.HasKey(history => history.Id);
            entity.HasIndex(history => new { history.OrderId, history.OccurredOnUtc });
            entity.Property(history => history.Actor).HasMaxLength(200);
            entity.Property(history => history.Reason).HasMaxLength(1000);
        });

        modelBuilder.Entity<OrderFulfillmentStatusHistory>(entity =>
        {
            entity.ToTable("order_fulfillment_status_history");
            entity.HasKey(history => history.Id);
            entity.HasIndex(history => new { history.OrderId, history.OccurredOnUtc });
            entity.HasIndex(history => new { history.OrderId, history.IdempotencyKey })
                .IsUnique()
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
            entity.Property(history => history.CarrierCode).HasMaxLength(50);
            entity.Property(history => history.CarrierName).HasMaxLength(120);
            entity.Property(history => history.TrackingNumber).HasMaxLength(120);
            entity.Property(history => history.Note).HasMaxLength(1000);
            entity.Property(history => history.Actor).HasMaxLength(200);
            entity.Property(history => history.IdempotencyKey).HasMaxLength(100).HasComment("履约操作幂等键");
            entity.Property(history => history.RequestFingerprint).HasMaxLength(64).HasComment("履约操作请求指纹");
        });
    }

    private static void ConfigureCatalogProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderProductProjection>(entity =>
        {
            entity.ToTable("product_projections", table =>
                table.HasCheckConstraint("CK_product_projections_price", "\"Price\" >= 0"));
            entity.HasKey(product => product.ProductId);
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.HasIndex(product => new { product.IsActive, product.CategoryId });
            entity.Property(product => product.Sku).HasMaxLength(80);
            entity.Property(product => product.Name).HasMaxLength(200);
            entity.Property(product => product.Price).HasPrecision(18, 2).HasComment("下单侧使用的目录单价投影");
            entity.Property(product => product.Currency).HasMaxLength(3);
        });

        modelBuilder.Entity<FlashSaleCampaignProjection>(entity =>
        {
            entity.ToTable("flash_sale_campaign_projections", table =>
            {
                table.HasCheckConstraint("CK_flash_sale_campaign_projections_limit", "\"PurchaseLimit\" > 0");
                table.HasCheckConstraint("CK_flash_sale_campaign_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
            });
            entity.HasKey(campaign => campaign.CampaignId);
            entity.Property(campaign => campaign.Sku).HasMaxLength(80);
            entity.Property(campaign => campaign.Strategy).HasMaxLength(30);
            entity.Property(campaign => campaign.Version).IsConcurrencyToken();
            entity.HasIndex(campaign => new { campaign.IsOpen, campaign.StartsOnUtc, campaign.EndsOnUtc });
        });
    }

    private static void ConfigurePromotionProjections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PromotionProjection>(entity =>
        {
            entity.ToTable("promotion_projections", table =>
            {
                table.HasCheckConstraint("CK_promotion_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                table.HasCheckConstraint("CK_promotion_projections_revision", "\"SourceRevision\" >= 0");
            });
            entity.HasKey(promotion => promotion.Id);
            entity.HasIndex(promotion => promotion.Code).IsUnique();
            entity.HasIndex(promotion => new { promotion.Status, promotion.StartsOnUtc, promotion.EndsOnUtc, promotion.Priority });
            entity.Property(promotion => promotion.Code).HasMaxLength(80);
            entity.Property(promotion => promotion.Name).HasMaxLength(200);
            entity.Property(promotion => promotion.TriggerType).HasMaxLength(30);
            entity.Property(promotion => promotion.Status).HasMaxLength(30);
            entity.Property(promotion => promotion.StackPolicy).HasMaxLength(30);
            entity.Property(promotion => promotion.ExclusiveGroup).HasMaxLength(80);
            entity.Property(promotion => promotion.SourceRevision).HasComment("已应用的促销业务修订号");
            entity.Property(promotion => promotion.DefinitionChecksum).HasMaxLength(64).HasComment("促销定义快照的 SHA-256 校验值");
            entity.Property(promotion => promotion.RulesJson).HasColumnType("jsonb");
            entity.Property(promotion => promotion.BenefitsJson).HasColumnType("jsonb");
            entity.Property(promotion => promotion.TargetsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CouponProjection>(entity =>
        {
            entity.ToTable("coupon_projections", table =>
            {
                table.HasCheckConstraint("CK_coupon_projections_limits", "(\"TotalLimit\" IS NULL OR \"TotalLimit\" > 0) AND \"PerUserLimit\" > 0");
                table.HasCheckConstraint("CK_coupon_projections_window", "\"ValidFromUtc\" < \"ValidUntilUtc\"");
            });
            entity.HasKey(coupon => coupon.Id);
            entity.HasIndex(coupon => coupon.CodeDigest).IsUnique();
            entity.HasIndex(coupon => new { coupon.PromotionId, coupon.Status });
            entity.Property(coupon => coupon.CodeDigest).HasMaxLength(64).HasComment("标准化券码的 SHA-256 摘要");
            entity.Property(coupon => coupon.MaskedCode).HasMaxLength(80).HasComment("用于展示和审计的脱敏券码");
            entity.Property(coupon => coupon.Status).HasMaxLength(30);
            entity.Property(coupon => coupon.AssignedUserId).HasMaxLength(200);
            entity.Property(coupon => coupon.QuotaStrategy).HasMaxLength(30).HasComment("券配额的单行或分桶并发策略");
            entity.Property(coupon => coupon.SourceRevision).HasComment("已应用的优惠券业务修订号");
            entity.HasMany(coupon => coupon.QuotaBuckets)
                .WithOne()
                .HasForeignKey(bucket => bucket.CouponId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(coupon => coupon.QuotaBuckets).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<CouponQuotaBucket>(entity =>
        {
            entity.ToTable("coupon_quota_buckets", table =>
            {
                table.HasCheckConstraint("CK_coupon_quota_buckets_capacity", "\"Capacity\" > 0");
                table.HasCheckConstraint("CK_coupon_quota_buckets_counters", "\"ReservedCount\" >= 0 AND \"ConsumedCount\" >= 0 AND \"ReservedCount\" + \"ConsumedCount\" <= \"Capacity\"");
            });
            entity.HasKey(bucket => bucket.Id);
            entity.HasIndex(bucket => new { bucket.CouponId, bucket.BucketNumber }).IsUnique();
            entity.Property(bucket => bucket.Capacity).HasComment("本地配额桶可核销总量");
            entity.Property(bucket => bucket.ReservedCount).HasComment("已为未完成订单预留的数量");
            entity.Property(bucket => bucket.ConsumedCount).HasComment("已由确认订单核销的数量");
            entity.Property(bucket => bucket.Version).IsConcurrencyToken().HasComment("配额桶乐观并发版本号");
        });

        modelBuilder.Entity<OrderCouponRedemption>(entity =>
        {
            entity.ToTable("order_coupon_redemptions", table =>
            {
                table.HasCheckConstraint("CK_order_coupon_redemptions_amount", "\"DiscountAmount\" > 0");
                table.HasCheckConstraint("CK_order_coupon_redemptions_status", "\"Status\" BETWEEN 0 AND 2");
            });
            entity.HasKey(redemption => redemption.Id);
            entity.HasIndex(redemption => new { redemption.OrderId, redemption.CouponId }).IsUnique();
            entity.HasIndex(redemption => new { redemption.CouponId, redemption.UserId, redemption.Status });
            entity.HasIndex(redemption => new { redemption.Status, redemption.ExpiresOnUtc });
            entity.Property(redemption => redemption.UserId).HasMaxLength(200);
            entity.Property(redemption => redemption.MaskedCode).HasMaxLength(80);
            entity.Property(redemption => redemption.QuotaBucketId).HasComment("本次核销占用的配额桶标识");
            entity.Property(redemption => redemption.DiscountAmount).HasPrecision(18, 2).HasComment("该优惠券为订单抵扣的金额");
            entity.Property(redemption => redemption.Status).HasComment("券配额的预留、核销或释放状态");
            entity.Property(redemption => redemption.ExpiresOnUtc).HasComment("未支付订单释放券配额的 UTC 时间");
            entity.Property(redemption => redemption.Version).IsConcurrencyToken().HasComment("券核销记录乐观并发版本号");
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(redemption => redemption.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CouponProjection>()
                .WithMany()
                .HasForeignKey(redemption => redemption.CouponId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CouponQuotaBucket>()
                .WithMany()
                .HasForeignKey(redemption => redemption.QuotaBucketId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
