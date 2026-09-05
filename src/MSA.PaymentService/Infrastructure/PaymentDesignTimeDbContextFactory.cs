using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MSA.Infrastrucute;
using MSA.PaymentService.Application;

namespace MSA.PaymentService.Infrastructure;

public sealed class PaymentDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<PaymentDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblyContaining<ConfirmPaymentCommand>();
        });
    }
}
