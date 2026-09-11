using System.Security.Cryptography.X509Certificates;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollection
{
    internal static IServiceCollection AddIdentityDefault(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.SignIn.RequireConfirmedEmail = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }

    internal static IServiceCollection AddOpenIddictServer(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOpenIddict()
            .AddCore(options => {
                options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>();
                options.UseQuartz();
            })
            .AddServer(options =>
            {
                var issuer = configuration["Authentication:Issuer"];
                if (!string.IsNullOrWhiteSpace(issuer))
                    options.SetIssuer(new Uri(issuer));

                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.SetEndSessionEndpointUris("connect/logout");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.AllowRefreshTokenFlow();
                options.AllowClientCredentialsFlow();

                options.SetAccessTokenLifetime(
                    TimeSpan.FromMinutes(15));

                options.SetRefreshTokenLifetime(
                    TimeSpan.FromDays(14));

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    IdentityServiceConstants.Scopes.Catalog,
                    IdentityServiceConstants.Scopes.Inventory,
                    IdentityServiceConstants.Scopes.User,
                    IdentityServiceConstants.Scopes.Payment,
                    IdentityServiceConstants.Scopes.Basket,
                    IdentityServiceConstants.Scopes.Notification,
                    IdentityServiceConstants.Scopes.Order);
                if (environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();
                }
                else
                {
                    var path = configuration["Certificates:Path"];
                    var password = configuration["Certificates:Password"];
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("A production OpenIddict certificate is required.");
                    var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
                    options.AddEncryptionCertificate(certificate).AddSigningCertificate(certificate);
                }

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }
}
