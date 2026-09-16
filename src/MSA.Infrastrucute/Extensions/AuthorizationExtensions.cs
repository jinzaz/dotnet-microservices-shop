using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace MSA.Infrastrucute.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddMsaApiAuthorization(
        this IServiceCollection services,
        string apiScope,
        string adminReadScope,
        string adminWriteScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminReadScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminWriteScope);

        return services.AddAuthorization(options =>
        {
            options.AddPolicy(apiScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(apiScope)));
            options.AddPolicy(adminReadScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminReadScope)));
            options.AddPolicy(adminWriteScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminWriteScope)));

        });
    }

    public static bool HasMsaScope(this ClaimsPrincipal principal, string scope) =>
        principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
