using MSA.Common;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.IdentityService;

internal sealed class IdentitySeedHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in new[] { "admin", "customer", "operator" })
        {
            if (!await roles.RoleExistsAsync(roleName))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(roleName)));
        }

        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var (name, displayName, resource) in new[]
        {
            (IdentityServiceConstants.Scopes.Auth, "Auth API", IdentityServiceConstants.Resources.Auth),
            (IdentityServiceConstants.Scopes.AuthAdminRead, "Auth Admin Read", IdentityServiceConstants.Resources.Auth),
            (IdentityServiceConstants.Scopes.AuthAdminWrite, "Auth Admin Write", IdentityServiceConstants.Resources.Auth),

            (IdentityServiceConstants.Scopes.User, "User API", IdentityServiceConstants.Resources.User),
            (IdentityServiceConstants.Scopes.UserAdminRead, "User Admin Read", IdentityServiceConstants.Resources.User),
            (IdentityServiceConstants.Scopes.UserAdminWrite, "User Admin Write", IdentityServiceConstants.Resources.User),

            (IdentityServiceConstants.Scopes.Catalog, "Catalog API", IdentityServiceConstants.Resources.Catalog),
            (IdentityServiceConstants.Scopes.CatalogAdminRead, "Catalog Admin Read", IdentityServiceConstants.Resources.Catalog),
            (IdentityServiceConstants.Scopes.CatalogAdminWrite, "Catalog Admin Write", IdentityServiceConstants.Resources.Catalog),


            (IdentityServiceConstants.Scopes.Inventory, "Inventory API", IdentityServiceConstants.Resources.Inventory),
            (IdentityServiceConstants.Scopes.InventoryAdminRead, "Inventory Admin Read", IdentityServiceConstants.Resources.Inventory),
            (IdentityServiceConstants.Scopes.InventoryAdminWrite, "Inventory Admin Write", IdentityServiceConstants.Resources.Inventory),


            (IdentityServiceConstants.Scopes.Notification, "Notification API", IdentityServiceConstants.Resources.Notification),
            (IdentityServiceConstants.Scopes.NotificationAdminRead, "Notification Admin Read", IdentityServiceConstants.Resources.Notification),
            (IdentityServiceConstants.Scopes.NotificationAdminWrite, "Notification Admin Write", IdentityServiceConstants.Resources.Notification),

            (IdentityServiceConstants.Scopes.Basket, "Basket API", IdentityServiceConstants.Resources.Basket),
            (IdentityServiceConstants.Scopes.BasketAdminRead, "Basket Admin Read", IdentityServiceConstants.Resources.Basket),
            (IdentityServiceConstants.Scopes.BasketAdminWrite, "Basket Admin Write", IdentityServiceConstants.Resources.Basket),

            (IdentityServiceConstants.Scopes.Order, "Order API", IdentityServiceConstants.Resources.Order),
            (IdentityServiceConstants.Scopes.OrderAdminRead, "Order Admin Read", IdentityServiceConstants.Resources.Order),
            (IdentityServiceConstants.Scopes.OrderAdminWrite, "Order Admin Write", IdentityServiceConstants.Resources.Order),

            (IdentityServiceConstants.Scopes.Payment, "Payment API", IdentityServiceConstants.Resources.Payment),
            (IdentityServiceConstants.Scopes.PaymentAdminRead, "Payment Admin Read", IdentityServiceConstants.Resources.Payment),
            (IdentityServiceConstants.Scopes.PaymentAdminWrite, "Payment Admin Write", IdentityServiceConstants.Resources.Payment)
        })
        {
            await UpsertScope(scopes, name, displayName, resource, cancellationToken);
        }

        var configuredApplications = configuration
            .GetSection("OpenIddict:Applications")
            .Get<OpenIddictApplicationSeed[]>() ?? [];
        if (configuredApplications.Length is 0)
            throw new InvalidOperationException("At least one OpenIddict:Applications entry is required.");

        var clientIds = new HashSet<string>(StringComparer.Ordinal);
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var application in configuredApplications)
        {
            Validate(application, clientIds);
            await UpsertApplication(applications, application, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task UpsertScope(
        IOpenIddictScopeManager scopes,
        string name,
        string displayName,
        string resource,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = displayName,
            Resources = { resource }
        };
        var existing = await scopes.FindByNameAsync(name, cancellationToken);
        if (existing is null)
            await scopes.CreateAsync(descriptor, cancellationToken);
        else
            await scopes.UpdateAsync(existing, descriptor, cancellationToken);
    }

    private static async Task UpsertApplication(
        IOpenIddictApplicationManager applications,
        OpenIddictApplicationSeed application,
        CancellationToken cancellationToken)
    {
        var descriptor = CreateDescriptor(application);
        var existing = await applications.FindByClientIdAsync(application.ClientId, cancellationToken);
        if (existing is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            return;
        }

        var current = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(current, existing, cancellationToken);

        // Preserve the stored hash while reconciling non-secret settings; rotate only when configured value changed.
        descriptor.ClientSecret = current.ClientSecret;
        await applications.UpdateAsync(existing, descriptor, cancellationToken);
        if (!await applications.ValidateClientSecretAsync(existing, application.ClientSecret, cancellationToken))
            await applications.UpdateAsync(existing, application.ClientSecret, cancellationToken);
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(OpenIddictApplicationSeed application)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = application.ClientId,
            ClientSecret = application.ClientSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ParseConsentType(application.ConsentType),
            DisplayName = string.IsNullOrWhiteSpace(application.DisplayName)
                ? application.ClientId
                : application.DisplayName
        };

        foreach (var value in application.RedirectUris)
            descriptor.RedirectUris.Add(ParseAbsoluteUri(value, application.ClientId, nameof(application.RedirectUris)));
        foreach (var value in application.PostLogoutRedirectUris)
            descriptor.PostLogoutRedirectUris.Add(ParseAbsoluteUri(value, application.ClientId, nameof(application.PostLogoutRedirectUris)));

        foreach (var flow in application.Flows.Distinct(StringComparer.Ordinal))
        {
            switch (flow)
            {
                case GrantTypes.AuthorizationCode:
                    descriptor.Permissions.UnionWith([
                        Permissions.Endpoints.Authorization,
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.AuthorizationCode,
                        Permissions.ResponseTypes.Code]);
                    descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
                    break;

                case GrantTypes.RefreshToken:
                    descriptor.Permissions.UnionWith([
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.RefreshToken]);
                    break;

                case GrantTypes.ClientCredentials:
                    descriptor.Permissions.UnionWith([
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials]);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"OpenIddict application '{application.ClientId}' uses unsupported flow '{flow}'.");
            }
        }

        if (application.PostLogoutRedirectUris.Length > 0)
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);

        foreach (var scope in application.Scopes.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var permission = GetScopePermission(scope);
            if (permission is not null)
                descriptor.Permissions.Add(permission);
        }

        return descriptor;
    }

    private static string? GetScopePermission(string scope) => scope switch
    {
        Scopes.OpenId or Scopes.OfflineAccess => null,
        Scopes.Address => Permissions.Scopes.Address,
        Scopes.Email => Permissions.Scopes.Email,
        Scopes.Phone => Permissions.Scopes.Phone,
        Scopes.Profile => Permissions.Scopes.Profile,
        Scopes.Roles => Permissions.Scopes.Roles,
        _ => Permissions.Prefixes.Scope + scope
    };

    private static string ParseConsentType(string value) => value switch
    {
        ConsentTypes.Explicit => ConsentTypes.Explicit,
        ConsentTypes.External => ConsentTypes.External,
        ConsentTypes.Implicit => ConsentTypes.Implicit,
        ConsentTypes.Systematic => ConsentTypes.Systematic,
        _ => throw new InvalidOperationException($"Unsupported OpenIddict consent type '{value}'.")
    };

    private static Uri ParseAbsoluteUri(string value, string clientId, string property)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"OpenIddict application '{clientId}' has an invalid {property} value '{value}'.");
        return uri;
    }

    private static void Validate(OpenIddictApplicationSeed application, ISet<string> clientIds)
    {
        if (string.IsNullOrWhiteSpace(application.ClientId))
            throw new InvalidOperationException("Every OpenIddict application requires a ClientId.");
        if (!clientIds.Add(application.ClientId))
            throw new InvalidOperationException($"Duplicate OpenIddict client id '{application.ClientId}'.");
        if (string.IsNullOrWhiteSpace(application.ClientSecret))
        {
            throw new InvalidOperationException(
                $"OpenIddict application '{application.ClientId}' requires a ClientSecret. " +
                $"Set {MsaEnvironment.ClientSecretVariable(application.ClientId)} in the environment or in msa.env.");
        }
        if (application.Flows.Length is 0)
            throw new InvalidOperationException($"OpenIddict application '{application.ClientId}' requires at least one flow.");
        if (application.Flows.Contains(GrantTypes.AuthorizationCode, StringComparer.Ordinal) &&
            application.RedirectUris.Length is 0)
        {
            throw new InvalidOperationException(
                $"Authorization-code application '{application.ClientId}' requires a redirect URI.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}

internal sealed record OpenIddictApplicationSeed
{
    public string ClientId { get; init; } = string.Empty;
    // 只读环境变量 MSA_CLIENT_SECRET_<ClientId 大写>，不从配置绑定：与取令牌的服务读同一个变量。
    public string ClientSecret => MsaEnvironment.ClientSecret(ClientId) ?? string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ConsentType { get; init; } = ConsentTypes.Implicit;
    public string[] Flows { get; init; } = [];
    public string[] Scopes { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
}
