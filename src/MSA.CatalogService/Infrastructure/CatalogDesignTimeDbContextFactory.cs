using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.CatalogService.Application;
using MSA.Infrastrucute;

namespace MSA.CatalogService.Infrastructure;

public sealed class CatalogDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<CatalogDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblyContaining<CreateDraftProductCommand>();
        });
    }
}
