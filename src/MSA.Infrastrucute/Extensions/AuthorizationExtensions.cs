using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace MSA.Infrastrucute.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddMsaApiAuthorization(
        this IServiceCollection services,
        string apiAudience,
        string adminReadScope,
        string adminWriteScope,
        string? sharedReadPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiAudience);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminReadScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminWriteScope);

        return services.AddAuthorization(options =>
        {
            options.AddPolicy(apiAudience, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("aud", apiAudience));
            options.AddPolicy(adminReadScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminReadScope)));
            options.AddPolicy(adminWriteScope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.HasMsaScope(adminWriteScope)));

            if (!string.IsNullOrWhiteSpace(sharedReadPolicy))
            {
                options.AddPolicy(sharedReadPolicy, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.HasClaim("aud", apiAudience) ||
                        context.User.HasMsaScope(adminReadScope)));
            }
        });
    }

    public static bool HasMsaScope(this ClaimsPrincipal principal, string scope) =>
        principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
