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
    public DbSet<FlashSaleCampaign> FlashSaleCampaigns => Set<FlashSaleCampaign>();
    public DbSet<FlashSaleStockBucket> FlashSaleStockBuckets => Set<FlashSaleStockBucket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.ToTable("stock_items", table =>
                table.HasCheckConstraint(
                    "CK_stock_items_quantities",
                    "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\""));
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
                table.HasCheckConstraint("CK_flash_sale_stock_buckets_allocated", "\"AllocatedQuantity\" > 0");
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
                table.HasCheckConstraint(
                    "CK_inventory_reservations_flash_sale_metadata",
                    "(\"FlashSaleCampaignId\" IS NULL AND \"FlashSaleStockBucketId\" IS NULL AND \"UserId\" IS NULL) OR (\"FlashSaleCampaignId\" IS NOT NULL AND \"FlashSaleStockBucketId\" IS NOT NULL AND \"UserId\" IS NOT NULL)"));
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OrderId).IsUnique();
            entity.HasIndex(value => new { value.FlashSaleCampaignId, value.UserId })
                .IsUnique()
                .HasFilter("\"FlashSaleCampaignId\" IS NOT NULL");
            entity.Property(value => value.UserId).HasMaxLength(200);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.Ignore(value => value.IsFlashSale);
            entity.HasMany(value => value.Lines)
                .WithOne()
                .HasForeignKey("ReservationId");
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
            entity.Property(value => value.Sku).HasMaxLength(80);
        });
    }
}
