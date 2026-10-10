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
        // LIKE '%片段%' 走这个 GIN 三元组索引；CONCURRENTLY 建索引不锁写入（下单预占照常进行）。
        entity.HasIndex(value => value.Sku, "IX_inventory_stock_items_Sku_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .IsCreatedConcurrently();
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("标准化商品 SKU");
        entity.Property(value => value.OnHand).HasComment("当前实物库存总量");
        entity.Property(value => value.Reserved).HasComment("已预占但尚未扣减的库存量");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("库存乐观并发版本号");
        entity.Ignore(value => value.Available);
    }
}
internal sealed class InventoryAllocationConfiguration : IEntityTypeConfiguration<InventoryAllocation>
{
    public void Configure(EntityTypeBuilder<InventoryAllocation> entity)
    {
        entity.ToTable("inventory_allocations", table =>
        {
            table.HasCheckConstraint("CK_inventory_allocations_quantity", "\"TotalQuantity\" > 0");
            table.HasCheckConstraint("CK_inventory_allocations_limit", "\"PerUserLimit\" > 0 AND \"PerUserLimit\" <= \"TotalQuantity\"");
            table.HasCheckConstraint("CK_inventory_allocations_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
            table.HasCheckConstraint(
                "CK_inventory_allocations_counters",
                "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"TotalQuantity\"");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex(value => new { value.OfferId, value.Sku }).IsUnique();
        entity.HasIndex(value => new { value.Status, value.EndsOnUtc });
        entity.Property(value => value.OfferId).HasComment("目录促销或限量销售标识");
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("配额对应的标准化 SKU");
        entity.Property(value => value.TotalQuantity).HasComment("从常规库存圈定的总数量");
        entity.Property(value => value.ReservedQuantity).HasComment("已被未支付订单预占的数量");
        entity.Property(value => value.SoldQuantity).HasComment("已支付并正式扣减的数量");
        entity.Property(value => value.ReleasedQuantity).HasComment("关闭后已归还常规库存的数量");
        entity.Property(value => value.PerUserLimit).HasComment("单用户可同时预占及累计售出的数量上限");
        entity.Property(value => value.StartsOnUtc).HasComment("配额生效的 UTC 时间");
        entity.Property(value => value.EndsOnUtc).HasComment("配额失效的 UTC 时间");
        entity.Property(value => value.Status).HasComment("配额状态：开放或关闭");
        entity.Property(value => value.CreatedOnUtc).HasComment("配额创建的 UTC 时间");
        entity.Property(value => value.ClosedOnUtc).HasComment("配额关闭的 UTC 时间");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("配额乐观并发版本号");
        entity.Ignore(value => value.AvailableQuantity);
    }
}

internal sealed class InventoryAllocationBuyerConfiguration : IEntityTypeConfiguration<InventoryAllocationBuyer>
{
    public void Configure(EntityTypeBuilder<InventoryAllocationBuyer> entity)
    {
        entity.ToTable("inventory_allocation_buyers", table =>
            table.HasCheckConstraint(
                "CK_inventory_allocation_buyers_counters",
                "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0"));
        entity.HasKey(value => new { value.AllocationId, value.UserId });
        entity.Property(value => value.AllocationId).HasComment("库存配额标识");
        entity.Property(value => value.UserId).HasMaxLength(200).HasComment("统一认证平台用户标识");
        entity.Property(value => value.ReservedQuantity).HasComment("该用户当前预占数量");
        entity.Property(value => value.SoldQuantity).HasComment("该用户累计售出数量");
        entity.Property(value => value.ReleasedQuantity).HasComment("该用户累计释放数量");
        entity.Property(value => value.CreatedOnUtc).HasComment("用户计数创建的 UTC 时间");
        entity.Property(value => value.UpdatedOnUtc).HasComment("用户计数更新的 UTC 时间");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("用户配额计数乐观并发版本号");
        entity.HasOne<InventoryAllocation>()
            .WithMany()
            .HasForeignKey(value => value.AllocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> entity)
    {
        entity.ToTable("inventory_reservations", table =>
        {
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
        entity.Property(value => value.OrderId).HasComment("预占关联的订单标识，也是业务唯一键");
        entity.Property(value => value.UserId).HasMaxLength(200).HasComment("预占所属用户；历史普通预占可能为空");
        entity.Property(value => value.Status).HasComment("预占状态：已预占、已提交或已释放");
        entity.Property(value => value.ExpiresOnUtc).HasComment("预占自动释放的 UTC 截止时间");
        entity.Property(value => value.CommittedOnUtc).HasComment("库存正式扣减的 UTC 时间");
        entity.Property(value => value.ReleasedOnUtc).HasComment("库存释放的 UTC 时间");
        entity.Property(value => value.TerminalReason).HasMaxLength(1000).HasComment("预占释放的业务原因");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("预占乐观并发版本号");
        entity.Ignore(value => value.HasAllocations);
        entity.HasMany(value => value.Lines)
            .WithOne()
            .HasForeignKey("ReservationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class InventoryReservationLineConfiguration : IEntityTypeConfiguration<InventoryReservationLine>
{
    public void Configure(EntityTypeBuilder<InventoryReservationLine> entity)
    {
        entity.ToTable("inventory_reservation_lines", table =>
        {
            table.HasCheckConstraint("CK_inventory_reservation_lines_quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint(
                "CK_inventory_reservation_lines_allocation",
                "(\"AllocationId\" IS NULL AND \"OfferId\" IS NULL) OR (\"AllocationId\" IS NOT NULL AND \"OfferId\" IS NOT NULL)");
        });
        entity.HasKey(value => value.Id);
        entity.HasIndex("ReservationId", nameof(InventoryReservationLine.Sku)).IsUnique();
        entity.Property(value => value.Sku).HasMaxLength(80).HasComment("预占商品的标准化 SKU");
        entity.Property(value => value.Quantity).HasComment("该商品的预占数量");
        entity.Property(value => value.AllocationId).HasComment("该订单行使用的库存配额标识；普通库存为空");
        entity.Property(value => value.OfferId).HasComment("该订单行选择的促销或限量销售标识；普通库存为空");
        entity.HasIndex(value => new { value.AllocationId, value.OfferId });
        entity.HasOne<InventoryAllocation>()
            .WithMany()
            .HasForeignKey(value => value.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);
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
                "\"Type\" BETWEEN 0 AND 7");
            table.HasCheckConstraint(
                "CK_stock_movements_reference_type",
                "\"ReferenceType\" BETWEEN 0 AND 3");
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
        entity.Property(value => value.ReferenceId).HasComment("关联的预占、秒杀活动或售后单标识");
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
