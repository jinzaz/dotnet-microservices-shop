using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.Infrastrucute;
using MSA.OrderService.Application.CreateOrder;

namespace MSA.OrderService.Infrastructure;

public sealed class OrderDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<OrderDbContext>
{

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblyContaining<CreateOrderCommand>();
        });
    }
}
