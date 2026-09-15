using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.InventoryService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.InventoryService.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator, "inventory")
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();
    public DbSet<InventoryRequestDecision> RequestDecisions => Set<InventoryRequestDecision>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryAllocation> InventoryAllocations => Set<InventoryAllocation>();
    public DbSet<InventoryAllocationBuyer> InventoryAllocationBuyers => Set<InventoryAllocationBuyer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}
