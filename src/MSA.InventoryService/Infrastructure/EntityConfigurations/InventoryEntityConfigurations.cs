using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.InventoryService.Domain;

namespace MSA.InventoryService.Infrastructure.EntityConfigurations;
internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> entity)
    {
        entity.ToTable("inventory_stock_items", table =>
        {
            table.HasCheckConstraint(
                "CK_stock_items_quantities",
                "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
            table.HasCheckConstraint("CK_stock_items_sku", "\"Sku\" = upper(trim(\"Sku\"))");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => value.Sku).IsUnique();
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("标准化商品 SKU");
        entity.Property(value => value.OnHand).HasComment("当前实物库存总量");
        entity.Property(value => value.Reserved).HasComment("已预占但尚未扣减的库存量");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("库存乐观并发版本号");
        entity.Ignore(value => value.Available);
    }
}
internal sealed class FlashSaleCampaignConfiguration : IEntityTypeConfiguration<FlashSaleCampaign>
{
    public void Configure(EntityTypeBuilder<FlashSaleCampaign> entity)
    {
        entity.ToTable("inventory_flash_sale_campaigns", table =>
        {
            table.HasCheckConstraint("CK_flash_sale_campaigns_quantity", "\"TotalQuantity\" > 0");
            table.HasCheckConstraint("CK_flash_sale_campaigns_purchase_limit", "\"PurchaseLimit\" > 0 AND \"PurchaseLimit\" <= \"TotalQuantity\"");
            table.HasCheckConstraint("CK_flash_sale_campaigns_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
            table.HasCheckConstraint(
                "CK_flash_sale_campaigns_bucket_count",
                "\"BucketCount\" BETWEEN 1 AND 128 AND \"BucketCount\" * \"PurchaseLimit\" <= \"TotalQuantity\"");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("秒杀商品的标准化 SKU");
        entity.Property(value => value.TotalQuantity).HasComment("活动分配的库存总量");
        entity.Property(value => value.PurchaseLimit).HasComment("每个用户在本活动中的购买上限");
        entity.Property(value => value.Strategy).HasComment("秒杀库存采用单行或分桶策略");
        entity.Property(value => value.BucketCount).HasComment("用于分散热点写入的库存桶数量");
        entity.Property(value => value.Status).HasComment("秒杀活动状态");
        entity.Property(value => value.StartsOnUtc).HasComment("活动开始的 UTC 时间");
        entity.Property(value => value.EndsOnUtc).HasComment("活动结束的 UTC 时间");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("秒杀活动乐观并发版本号");
        entity.HasIndex(value => new { value.Status, value.EndsOnUtc });
        entity.HasMany(value => value.StockBuckets)
            .WithOne()
            .HasForeignKey(value => value.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(value => value.StockBuckets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class FlashSaleStockBucketConfiguration : IEntityTypeConfiguration<FlashSaleStockBucket>
{
    public void Configure(EntityTypeBuilder<FlashSaleStockBucket> entity)
    {
        entity.ToTable("inventory_flash_sale_stock_buckets", table =>
        {
            table.HasCheckConstraint("CK_flash_sale_stock_buckets_allocated", "\"AllocatedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_flash_sale_stock_buckets_counters",
                "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => new { value.CampaignId, value.BucketNumber }).IsUnique();
        entity.Property(value => value.BucketNumber).HasComment("活动内的库存桶编号");
        entity.Property(value => value.AllocatedQuantity).HasComment("分配给该桶的库存总量");
        entity.Property(value => value.ReservedQuantity).HasComment("该桶已预占的库存量");
        entity.Property(value => value.SoldQuantity).HasComment("该桶已确认售出的库存量");
        entity.Property(value => value.ReleasedQuantity).HasComment("活动关闭后不再销售的库存量");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("库存桶乐观并发版本号");
        entity.Ignore(value => value.AvailableQuantity);
    }
}
internal sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> entity)
    {
        entity.ToTable("inventory_reservations", table =>
        {
            table.HasCheckConstraint(
                "CK_inventory_reservations_flash_sale_metadata",
                "(\"FlashSaleCampaignId\" IS NULL AND \"FlashSaleStockBucketId\" IS NULL AND \"UserId\" IS NULL) OR (\"FlashSaleCampaignId\" IS NOT NULL AND \"FlashSaleStockBucketId\" IS NOT NULL AND \"UserId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_inventory_reservations_status",
                "\"Status\" BETWEEN 0 AND 2");
            table.HasCheckConstraint(
                "CK_inventory_reservations_timestamps",
                "\"ExpiresOnUtc\" > \"CreatedOnUtc\" AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            table.HasCheckConstraint(
                "CK_inventory_reservations_terminal_state",
                "(\"Status\" = 0 AND \"CommittedOnUtc\" IS NULL AND \"ReleasedOnUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR "
                + "(\"Status\" = 1 AND \"CommittedOnUtc\" IS NOT NULL AND \"ReleasedOnUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR "
                + "(\"Status\" = 2 AND \"CommittedOnUtc\" IS NULL AND \"ReleasedOnUtc\" IS NOT NULL AND \"TerminalReason\" IS NOT NULL)");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => value.OrderId).IsUnique();
        entity.HasIndex(value => new { value.Status, value.ExpiresOnUtc })
            .HasFilter("\"Status\" = 0");
        entity.HasIndex(value => new { value.Status, value.UpdatedOnUtc });
        entity.HasIndex(value => new { value.FlashSaleCampaignId, value.UserId })
            .IsUnique()
            .HasFilter("\"FlashSaleCampaignId\" IS NOT NULL");
        entity.Property(value => value.OrderId).HasComment("预占关联的订单标识，也是业务唯一键");
        entity.Property(value => value.UserId).HasMaxLength(200).HasComment("秒杀预占所属用户；普通预占为空");
        entity.Property(value => value.FlashSaleCampaignId).HasComment("秒杀活动标识；普通预占为空");
        entity.Property(value => value.FlashSaleStockBucketId).HasComment("秒杀库存桶标识；普通预占为空");
        entity.Property(value => value.Status).HasComment("预占状态：已预占、已提交或已释放");
        entity.Property(value => value.ExpiresOnUtc).HasComment("预占自动释放的 UTC 截止时间");
        entity.Property(value => value.CommittedOnUtc).HasComment("库存正式扣减的 UTC 时间");
        entity.Property(value => value.ReleasedOnUtc).HasComment("库存释放的 UTC 时间");
        entity.Property(value => value.TerminalReason).HasMaxLength(1000).HasComment("预占释放的业务原因");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("预占乐观并发版本号");
        entity.Ignore(value => value.IsFlashSale);
        entity.HasMany(value => value.Lines)
            .WithOne()
            .HasForeignKey("ReservationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<FlashSaleCampaign>()
            .WithMany()
            .HasForeignKey(value => value.FlashSaleCampaignId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FlashSaleStockBucket>()
            .WithMany()
            .HasForeignKey(value => value.FlashSaleStockBucketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class InventoryReservationLineConfiguration : IEntityTypeConfiguration<InventoryReservationLine>
{
    public void Configure(EntityTypeBuilder<InventoryReservationLine> entity)
    {
        entity.ToTable("inventory_reservation_lines", table =>
            table.HasCheckConstraint("CK_inventory_reservation_lines_quantity", "\"Quantity\" > 0"));
        entity.HasKey(value => value.Id);
        entity.HasIndex("ReservationId", nameof(InventoryReservationLine.Sku)).IsUnique();
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("预占商品的标准化 SKU");
        entity.Property(value => value.Quantity).HasComment("该商品的预占数量");
    }
}
internal sealed class InventoryRequestDecisionConfiguration : IEntityTypeConfiguration<InventoryRequestDecision>
{
    public void Configure(EntityTypeBuilder<InventoryRequestDecision> entity)
    {
        entity.ToTable("inventory_request_decisions", table =>
        {
            table.HasCheckConstraint(
                "CK_inventory_request_decisions_status",
                "\"Status\" BETWEEN 0 AND 1");
            table.HasCheckConstraint(
                "CK_inventory_request_decisions_hash",
                "char_length(\"RequestHash\") = 64");
            table.HasCheckConstraint(
                "CK_inventory_request_decisions_result",
                "(\"Status\" = 0 AND \"ReservationId\" IS NOT NULL AND \"RejectionReason\" IS NULL) OR "
                + "(\"Status\" = 1 AND \"ReservationId\" IS NULL AND \"RejectionReason\" IS NOT NULL)");
        });
        entity.HasKey(value => value.OrderId);
        entity.HasIndex(value => value.ReservationId)
            .IsUnique()
            .HasFilter("\"ReservationId\" IS NOT NULL");
        entity.HasIndex(value => new { value.Status, value.DecidedOnUtc });
        entity.Property(value => value.OrderId).HasComment("订单标识，也是库存申请的业务幂等键");
        entity.Property(value => value.RequestHash).HasMaxLength(64).IsFixedLength().HasComment("库存申请载荷的 SHA-256 摘要");
        entity.Property(value => value.Status).HasComment("库存申请的持久化决策状态");
        entity.Property(value => value.ReservationId).HasComment("申请成功后生成的预占标识");
        entity.Property(value => value.RejectionReason).HasMaxLength(1000).HasComment("库存申请拒绝原因");
        entity.Ignore(value => value.IsAccepted);
        entity.HasOne<InventoryReservation>()
            .WithOne()
            .HasForeignKey<InventoryRequestDecision>(value => value.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> entity)
    {
        entity.ToTable("inventory_stock_movements", table =>
        {
            table.HasCheckConstraint(
                "CK_stock_movements_type",
                "\"Type\" BETWEEN 0 AND 6");
            table.HasCheckConstraint(
                "CK_stock_movements_reference_type",
                "\"ReferenceType\" BETWEEN 0 AND 2");
            table.HasCheckConstraint(
                "CK_stock_movements_delta",
                "\"OnHandDelta\" <> 0 OR \"ReservedDelta\" <> 0");
            table.HasCheckConstraint(
                "CK_stock_movements_balances",
                "\"OnHandAfter\" >= 0 AND \"ReservedAfter\" >= 0 AND \"ReservedAfter\" <= \"OnHandAfter\"");
            table.HasCheckConstraint(
                "CK_stock_movements_reference",
                "(\"ReferenceType\" = 0 AND \"ReferenceId\" IS NULL) OR (\"ReferenceType\" <> 0 AND \"ReferenceId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_stock_movements_idempotency",
                "(\"IdempotencyKey\" IS NULL AND \"RequestHash\" IS NULL) OR "
                + "(\"IdempotencyKey\" IS NOT NULL AND \"RequestHash\" IS NOT NULL AND \"OperatorId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_stock_movements_hash",
                "\"RequestHash\" IS NULL OR char_length(\"RequestHash\") = 64");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => new { value.OperatorId, value.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        entity.HasIndex(value => new { value.StockItemId, value.OccurredOnUtc });
        entity.HasIndex(value => new { value.Sku, value.OccurredOnUtc });
        entity.HasIndex(value => new { value.ReferenceType, value.ReferenceId });
        entity.HasIndex(value => new { value.Type, value.ReferenceId, value.StockItemId })
            .IsUnique()
            .HasFilter("\"ReferenceId\" IS NOT NULL");
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("发生库存变动的标准化 SKU");
        entity.Property(value => value.Type).HasComment("库存变动业务类型");
        entity.Property(value => value.OnHandDelta).HasComment("实物库存变化量");
        entity.Property(value => value.ReservedDelta).HasComment("预占库存变化量");
        entity.Property(value => value.OnHandAfter).HasComment("变动后的实物库存余额");
        entity.Property(value => value.ReservedAfter).HasComment("变动后的预占库存余额");
        entity.Property(value => value.ReferenceType).HasComment("关联业务单据类型");
        entity.Property(value => value.ReferenceId).HasComment("关联的预占或秒杀活动标识");
        entity.Property(value => value.Reason).HasMaxLength(500).HasComment("库存变动原因");
        entity.Property(value => value.OperatorId).HasMaxLength(200).HasComment("发起库存变动的主体标识");
        entity.Property(value => value.IdempotencyKey).HasMaxLength(100).HasComment("人工调整等请求的业务幂等键");
        entity.Property(value => value.RequestHash).HasMaxLength(64).IsFixedLength().HasComment("幂等请求载荷的 SHA-256 摘要");
        entity.HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(value => value.StockItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
