using MediatR; using Microsoft.EntityFrameworkCore; using MSA.InventoryService.Domain; using MSA.Repositories.Persistence;
namespace MSA.InventoryService.Infrastructure;
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> o,IMediator m):MessagingDbContext(o,m)
{
 public DbSet<StockItem> StockItems=>Set<StockItem>(); public DbSet<InventoryReservation> Reservations=>Set<InventoryReservation>();
 protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);b.Entity<StockItem>(e=>{e.ToTable("stock_items");e.HasKey(x=>x.Id);e.HasIndex(x=>x.Sku).IsUnique();e.Property(x=>x.Sku).HasMaxLength(80);e.Property(x=>x.Version).IsConcurrencyToken();e.Ignore(x=>x.Available);});b.Entity<InventoryReservation>(e=>{e.ToTable("inventory_reservations");e.HasKey(x=>x.Id);e.HasIndex(x=>x.OrderId).IsUnique();e.HasMany(x=>x.Lines).WithOne().HasForeignKey("ReservationId");});b.Entity<InventoryReservationLine>(e=>{e.ToTable("inventory_reservation_lines");e.HasKey(x=>x.Id);e.Property(x=>x.Sku).HasMaxLength(80);});}
}
