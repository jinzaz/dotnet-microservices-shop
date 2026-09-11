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
            if (!await roles.RoleExistsAsync(roleName))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(roleName)));

        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.User, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.User, DisplayName = "User API", Resources = { "user-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Catalog, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Catalog, DisplayName = "Catalog API", Resources = { "catalog-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Inventory, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Inventory, DisplayName = "Inventory API", Resources = { "inventory-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Notification, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Notification, DisplayName = "Notification API", Resources = { "notification-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Basket, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Basket, DisplayName = "Basket API", Resources = { "basket-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Order, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Order, DisplayName = "Order API", Resources = { "order-api" } }, cancellationToken);
        }

        if (await scopes.FindByNameAsync(IdentityServiceConstants.Scopes.Payment, cancellationToken) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = IdentityServiceConstants.Scopes.Payment, DisplayName = "Payment API", Resources = { "payment-api" } }, cancellationToken);
        }

        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(IdentityServiceConstants.Applications.MSA_Bff_Web, cancellationToken) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = IdentityServiceConstants.Applications.MSA_Bff_Web,
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Explicit,
                DisplayName = "MSA Bff Web",
                ClientSecret = "AD4D63DB-17A5-4AD5-83F3-4A1C4DF7DCA6"
            };
            descriptor.RedirectUris.Add(new Uri("https://localhost:10008/bff/callback/login"));
            descriptor.PostLogoutRedirectUris.Add(new Uri("https://localhost:10008/bff/callback/logout"));
            descriptor.Permissions.UnionWith(new[]
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + "user",
                Permissions.Prefixes.Scope + "catalog",
                Permissions.Prefixes.Scope + "inventory",
                Permissions.Prefixes.Scope + "notification",
                Permissions.Prefixes.Scope + "basket",
                Permissions.Prefixes.Scope + "order",
                Permissions.Prefixes.Scope + "payment"
            });
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            await applications.CreateAsync(descriptor, cancellationToken);
        }

        var serviceClientId = configuration["OpenIddict:ServiceClientId"] ?? "msa-service";
        var serviceClientSecret = configuration["OpenIddict:ServiceClientSecret"];
        if (!string.IsNullOrWhiteSpace(serviceClientSecret) &&
            await applications.FindByClientIdAsync(serviceClientId, cancellationToken) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = serviceClientId,
                ClientSecret = serviceClientSecret,
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "MSA service client"
            };
            descriptor.Permissions.UnionWith(new[]
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "api"
            });
            await applications.CreateAsync(descriptor, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
