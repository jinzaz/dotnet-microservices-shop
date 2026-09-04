using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.CatalogService.Application;

namespace MSA.CatalogService.Infrastructure;

public sealed class CatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<CreateProductCommand>());
        services.AddDbContext<CatalogDbContext>(x => x.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgresql") ?? "Host=localhost;Database=catalog_design;Username=postgres;Password=design"));
        return services.BuildServiceProvider().GetRequiredService<CatalogDbContext>();
    }
}
