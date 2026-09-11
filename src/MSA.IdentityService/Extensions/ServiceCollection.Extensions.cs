using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollection
{
    internal static IServiceCollection AddIdentityDefault(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var identityUiBaseUrl = configuration["IdentityUi:BaseUrl"];
        if (!Uri.TryCreate(identityUiBaseUrl, UriKind.Absolute, out var identityUiBaseUri) ||
            identityUiBaseUri.Scheme != Uri.UriSchemeHttp && identityUiBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("IdentityUi:BaseUrl must be an absolute HTTP or HTTPS URI.");
        }

        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
            options.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.Name;
            options.ClaimsIdentity.RoleClaimType = OpenIddictConstants.Claims.Role;
            options.ClaimsIdentity.EmailClaimType = OpenIddictConstants.Claims.Email;
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

        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                var returnUrl = context.Properties.RedirectUri;
                if (string.IsNullOrWhiteSpace(returnUrl) ||
                    !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
                {
                    returnUrl = "/";
                }

                var loginUrl = new Uri(identityUiBaseUri, "login").AbsoluteUri;
                context.Response.Redirect(QueryHelpers.AddQueryString(loginUrl, "returnUrl", returnUrl));
                return Task.CompletedTask;
            };
        });

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
                var issuerValue = configuration["Authentication:Issuer"];
                if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer) ||
                    issuer.Scheme != Uri.UriSchemeHttp && issuer.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException(
                        "Authentication:Issuer must be an absolute HTTP or HTTPS URI.");
                }

                options.SetIssuer(issuer);

                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.SetEndSessionEndpointUris("/connect/logout");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.AllowRefreshTokenFlow();
                options.AllowClientCredentialsFlow();
                options.DisableAccessTokenEncryption();

                options.SetAccessTokenLifetime(
                    TimeSpan.FromMinutes(15));

                options.SetRefreshTokenLifetime(
                    TimeSpan.FromDays(14));

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Roles,
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

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        return services;
    }
}
