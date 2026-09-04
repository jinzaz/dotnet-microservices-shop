using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.IdentityService;

internal sealed class IdentitySeedHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in new[] { "admin", "customer", "operator" })
            if (!await roles.RoleExistsAsync(roleName))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(roleName)));

        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopes.FindByNameAsync("api", cancellationToken) is null)
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = "api", DisplayName = "MSA commerce API", Resources = { "api" } }, cancellationToken);

        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var clientId = configuration["OpenIddict:ClientId"] ?? "msa-web";
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "MSA web client"
            };
            descriptor.RedirectUris.Add(new Uri(configuration["OpenIddict:RedirectUri"] ?? "https://localhost:5173/signin-callback"));
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
                Permissions.Prefixes.Scope + "api",
                Permissions.Prefixes.Scope + "catalog.write",
                Permissions.Prefixes.Scope + "inventory.write",
                Permissions.Prefixes.Scope + "notifications.read.all"
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

        var adminEmail = configuration["BootstrapAdmin:Email"];
        var adminPassword = configuration["BootstrapAdmin:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(adminEmail);
            if (admin is null)
            {
                admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, DisplayName = "Administrator", EmailConfirmed = true };
                EnsureSucceeded(await users.CreateAsync(admin, adminPassword));
            }
            if (!await users.IsInRoleAsync(admin, "admin"))
                EnsureSucceeded(await users.AddToRoleAsync(admin, "admin"));
        }
        else if (environment.IsDevelopment())
        {
            // No hard-coded bootstrap password. Configure user-secrets when an initial admin is needed.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
