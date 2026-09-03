using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MSA.Infrastrucute.Mediator.Behaviors;

namespace MSA.Infrastrucute.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMsaApplicationPipeline(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidatorBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services;
    }
}
