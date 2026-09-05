using MSA.Infrastrucute;
using MSA.NotificationService.Integration;

namespace MSA.NotificationService.Infrastructure;

public sealed class NotificationDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<NotificationDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblyContaining<OrderConfirmedHandler>();
        });
    }
}
