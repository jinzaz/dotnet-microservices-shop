using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.OrderService.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderProductProjection> ProductProjections => Set<OrderProductProjection>();
    public DbSet<FlashSaleCampaignProjection> FlashSaleCampaignProjections => Set<FlashSaleCampaignProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders", table =>
                table.HasCheckConstraint("CK_orders_amount", "\"Amount\" >= 0"));
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.UserId, value.IdempotencyKey }).IsUnique();
            entity.HasIndex(value => new { value.FlashSaleCampaignId, value.UserId })
                .IsUnique()
                .HasFilter("\"FlashSaleCampaignId\" IS NOT NULL");
            entity.Property(value => value.IdempotencyKey).HasMaxLength(100);
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.Currency).HasMaxLength(3);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.HasMany(value => value.Lines)
                .WithOne()
                .HasForeignKey("OrderId");
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("order_lines", table =>
            {
                table.HasCheckConstraint("CK_order_lines_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_order_lines_unit_price", "\"UnitPrice\" >= 0");
            });
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Sku).HasMaxLength(80);
            entity.Property(value => value.ProductName).HasMaxLength(200);
            entity.Property(value => value.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OrderProductProjection>(entity =>
        {
            entity.ToTable("product_projections");
            entity.HasKey(value => value.ProductId);
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.Property(value => value.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<FlashSaleCampaignProjection>(entity =>
        {
            entity.ToTable("flash_sale_campaign_projections", table =>
            {
                table.HasCheckConstraint("CK_flash_sale_campaign_projections_limit", "\"PurchaseLimit\" > 0");
                table.HasCheckConstraint("CK_flash_sale_campaign_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
            });
            entity.HasKey(value => value.CampaignId);
            entity.Property(value => value.Sku).HasMaxLength(80);
            entity.Property(value => value.Strategy).HasMaxLength(30);
            entity.Property(value => value.Version).IsConcurrencyToken();
            entity.HasIndex(value => new { value.IsOpen, value.StartsOnUtc, value.EndsOnUtc });
        });
    }
}
