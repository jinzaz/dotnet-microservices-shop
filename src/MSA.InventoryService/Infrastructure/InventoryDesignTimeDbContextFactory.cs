using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.Infrastrucute;
using MSA.InventoryService.Application;

namespace MSA.InventoryService.Infrastructure;

public sealed class InventoryDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<InventoryDbContext>
{

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblyContaining<AddStockCommand>();
        });
    }
}
