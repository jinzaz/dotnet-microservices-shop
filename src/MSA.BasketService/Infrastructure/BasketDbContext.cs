using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.BasketService.Infrastructure;

public sealed class BasketDbContext(
    DbContextOptions<BasketDbContext> options,
    IMediator mediator) : MessagingDbContext(options, mediator, "basket")
{
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<BasketItem> BasketItems => Set<BasketItem>();
    public DbSet<BasketCoupon> BasketCoupons => Set<BasketCoupon>();
    public DbSet<BasketProductSnapshot> ProductSnapshots => Set<BasketProductSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasketDbContext).Assembly);
    }
}
