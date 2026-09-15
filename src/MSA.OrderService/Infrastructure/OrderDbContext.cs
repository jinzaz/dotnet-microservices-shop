using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.OrderService.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator, "order")
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderFulfillmentStatusHistory> OrderFulfillmentStatusHistory => Set<OrderFulfillmentStatusHistory>();
    public DbSet<OrderProductProjection> ProductProjections => Set<OrderProductProjection>();
    public DbSet<InventoryAllocationProjection> InventoryAllocationProjections => Set<InventoryAllocationProjection>();
    public DbSet<PromotionProjection> PromotionProjections => Set<PromotionProjection>();
    public DbSet<CouponProjection> CouponProjections => Set<CouponProjection>();
    public DbSet<CouponQuotaBucket> CouponQuotaBuckets => Set<CouponQuotaBucket>();
    public DbSet<OrderCouponRedemption> CouponRedemptions => Set<OrderCouponRedemption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
    }
}
