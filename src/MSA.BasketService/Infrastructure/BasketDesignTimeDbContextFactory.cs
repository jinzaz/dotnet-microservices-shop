using Microsoft.Extensions.DependencyInjection;
using MSA.BasketService.Application;
using MSA.Infrastrucute;

namespace MSA.BasketService.Infrastructure;

public sealed class BasketDesignTimeDbContextFactory
    : BaseDesignTimeDbContextFactory<BasketDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<GetBasketQuery>());
    }
}
