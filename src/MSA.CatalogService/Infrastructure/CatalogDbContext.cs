using MediatR; using Microsoft.EntityFrameworkCore; using MSA.CatalogService.Domain; using MSA.Repositories.Persistence;
namespace MSA.CatalogService.Infrastructure;
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IMediator mediator) : MessagingDbContext(options, mediator)
{
    public DbSet<Product> Products => Set<Product>(); public DbSet<Category> Categories => Set<Category>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Entity<Category>(e => { e.ToTable("categories"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(120); }); modelBuilder.Entity<Product>(e => { e.ToTable("products"); e.HasKey(x => x.Id); e.HasIndex(x => x.Sku).IsUnique(); e.Property(x => x.Sku).HasMaxLength(80); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Price).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Version).IsConcurrencyToken(); }); }
}
