using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.PaymentService.Application;

namespace MSA.PaymentService.Infrastructure;

public sealed class PaymentDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<ConfirmPaymentCommand>());
        services.AddDbContext<PaymentDbContext>(x => x.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgresql") ?? "Host=localhost;Database=payment_design;Username=postgres;Password=design"));
        return services.BuildServiceProvider().GetRequiredService<PaymentDbContext>();
    }
}
