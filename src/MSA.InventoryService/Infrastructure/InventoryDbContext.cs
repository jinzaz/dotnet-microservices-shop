using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.InventoryService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.InventoryService.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();
    public DbSet<InventoryRequestDecision> RequestDecisions => Set<InventoryRequestDecision>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<FlashSaleCampaign> FlashSaleCampaigns => Set<FlashSaleCampaign>();
    public DbSet<FlashSaleStockBucket> FlashSaleStockBuckets => Set<FlashSaleStockBucket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.ToTable("stock_items", table =>
            {
                table.HasCheckConstraint(
                    "CK_stock_items_quantities",
                    "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
                table.HasCheckConstraint("CK_stock_items_sku", "\"Sku\" = upper(trim(\"Sku\"))");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.Property(value => value.Sku).HasMaxLength(80);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.Ignore(value => value.Available);
        });

        modelBuilder.Entity<FlashSaleCampaign>(entity =>
        {
            entity.ToTable("flash_sale_campaigns", table =>
            {
                table.HasCheckConstraint("CK_flash_sale_campaigns_quantity", "\"TotalQuantity\" > 0");
                table.HasCheckConstraint("CK_flash_sale_campaigns_purchase_limit", "\"PurchaseLimit\" > 0 AND \"PurchaseLimit\" <= \"TotalQuantity\"");
                table.HasCheckConstraint("CK_flash_sale_campaigns_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                table.HasCheckConstraint(
                    "CK_flash_sale_campaigns_bucket_count",
                    "\"BucketCount\" BETWEEN 1 AND 128 AND \"BucketCount\" * \"PurchaseLimit\" <= \"TotalQuantity\"");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Sku).HasMaxLength(80);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.HasIndex(value => new { value.Status, value.EndsOnUtc });
            entity.HasMany(value => value.StockBuckets)
                .WithOne()
                .HasForeignKey(value => value.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(value => value.StockBuckets).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<FlashSaleStockBucket>(entity =>
        {
            entity.ToTable("flash_sale_stock_buckets", table =>
            {
                table.HasCheckConstraint("CK_flash_sale_stock_buckets_allocated", "\"AllocatedQuantity\" >= 0");
                table.HasCheckConstraint(
                    "CK_flash_sale_stock_buckets_counters",
                    "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.CampaignId, value.BucketNumber }).IsUnique();
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.Ignore(value => value.AvailableQuantity);
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
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
            entity.Property(value => value.UserId).HasMaxLength(200);
            entity.Property(value => value.TerminalReason).HasMaxLength(1000);
            entity.Property(value => value.Version).IsConcurrencyToken();
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
        });

        modelBuilder.Entity<InventoryReservationLine>(entity =>
        {
            entity.ToTable("inventory_reservation_lines", table =>
                table.HasCheckConstraint("CK_inventory_reservation_lines_quantity", "\"Quantity\" > 0"));
            entity.HasKey(value => value.Id);
            entity.HasIndex("ReservationId", nameof(InventoryReservationLine.Sku)).IsUnique();
            entity.Property(value => value.Sku).HasMaxLength(80);
        });

        modelBuilder.Entity<InventoryRequestDecision>(entity =>
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
            entity.Property(value => value.RequestHash).HasMaxLength(64).IsFixedLength();
            entity.Property(value => value.RejectionReason).HasMaxLength(1000);
            entity.Ignore(value => value.IsAccepted);
            entity.HasOne<InventoryReservation>()
                .WithOne()
                .HasForeignKey<InventoryRequestDecision>(value => value.ReservationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("stock_movements", table =>
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
            entity.Property(value => value.Sku).HasMaxLength(80);
            entity.Property(value => value.Reason).HasMaxLength(500);
            entity.Property(value => value.OperatorId).HasMaxLength(200);
            entity.Property(value => value.IdempotencyKey).HasMaxLength(100);
            entity.Property(value => value.RequestHash).HasMaxLength(64).IsFixedLength();
            entity.HasOne<StockItem>()
                .WithMany()
                .HasForeignKey(value => value.StockItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
