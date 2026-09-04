using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.NotificationService.Integration;

namespace MSA.NotificationService.Infrastructure;

public sealed class NotificationDesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<OrderConfirmedHandler>());
        services.AddDbContext<NotificationDbContext>(x => x.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgresql") ?? "Host=localhost;Database=notification_design;Username=postgres;Password=design"));
        return services.BuildServiceProvider().GetRequiredService<NotificationDbContext>();
    }
}
