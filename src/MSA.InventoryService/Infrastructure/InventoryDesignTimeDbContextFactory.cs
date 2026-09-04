using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.InventoryService.Application;

namespace MSA.InventoryService.Infrastructure;

public sealed class InventoryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<AddStockCommand>());
        services.AddDbContext<InventoryDbContext>(x => x.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgresql") ?? "Host=localhost;Database=inventory_design;Username=postgres;Password=design"));
        return services.BuildServiceProvider().GetRequiredService<InventoryDbContext>();
    }
}
