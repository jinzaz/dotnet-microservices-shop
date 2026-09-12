using Microsoft.Extensions.DependencyInjection;
using MSA.Infrastrucute;
using MSA.UserService.Application.Addresses;

namespace MSA.UserService.Infrastructure;

public sealed class UserDesignTimeDbContextFactory
    : BaseDesignTimeDbContextFactory<UserDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<CreateUserAddressCommand>());
    }
}
