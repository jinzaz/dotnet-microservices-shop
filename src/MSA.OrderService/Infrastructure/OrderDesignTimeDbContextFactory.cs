using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.OrderService.Application.CreateOrder;

namespace MSA.OrderService.Infrastructure;

public sealed class OrderDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgresql")
            ?? "Host=localhost;Database=order_design;Username=postgres;Password=design";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<CreateOrderCommand>());
        services.AddDbContext<OrderDbContext>(x => x.UseNpgsql(connection));
        return services.BuildServiceProvider().GetRequiredService<OrderDbContext>();
    }
}
