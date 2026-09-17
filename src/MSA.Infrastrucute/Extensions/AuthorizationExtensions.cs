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
            // The policy is named after the API resource (e.g. "catalog-api"), while the identity
            // service issues the consumer scope without the suffix (e.g. "catalog").
            var consumerScope = ConsumerScopeOf(apiScope);
            options.AddPolicy(apiScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    context.User.HasMsaScope(apiScope) || context.User.HasMsaScope(consumerScope)));
            options.AddPolicy(adminReadScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminReadScope)));
            options.AddPolicy(adminWriteScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminWriteScope)));

        });
    }

    private static string ConsumerScopeOf(string apiScope) =>
        apiScope.EndsWith("-api", StringComparison.Ordinal) || apiScope.EndsWith(".api", StringComparison.Ordinal)
            ? apiScope[..^4]
            : apiScope;

    public static bool HasMsaScope(this ClaimsPrincipal principal, string scope) =>
        principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
