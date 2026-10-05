using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MSA.Shared;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollection
{
    internal static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(IdentityServiceConstants.RateLimits.Authentication, context =>
                CreateIpLimiter(context, permitLimit: 10, TimeSpan.FromMinutes(1)));
            options.AddPolicy(IdentityServiceConstants.RateLimits.Registration, context =>
                CreateIpLimiter(context, permitLimit: 5, TimeSpan.FromHours(1)));
            options.AddPolicy(IdentityServiceConstants.RateLimits.SmsDispatch, context =>
                CreateIpLimiter(context, permitLimit: 5, TimeSpan.FromMinutes(10)));
        });

        return services;
    }

    internal static IServiceCollection AddIdentityDefault(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
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
            options.Lockout.AllowedForNewUsers = true;
            options.SignIn.RequireConfirmedEmail = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = environment.IsDevelopment()
                ? ".MSA.Identity.Session"
                : "__Host-MSA.Identity.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.None
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
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

        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = environment.IsDevelopment()
                ? ".MSA.Identity.Antiforgery"
                : "__Host-MSA.Identity.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.None
                : CookieSecurePolicy.Always;
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
                    IdentityServiceConstants.Scopes.AuthAdminRead,
                    IdentityServiceConstants.Scopes.AuthAdminWrite,
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
                    // 签名 / 加密令牌的证书：生产环境只从环境变量读取。
                    var certificate = OpenIddictCertificateEnvironment.Load();
                    options.AddEncryptionCertificate(certificate).AddSigningCertificate(certificate);
                }

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                // 非开发环境 TLS 终止在 nginx：公网请求带 X-Forwarded-Proto=https，但 BFF/服务经 MSA_IDENTITY_INTERNAL_ADDRESS
                // 直连容器的 /connect/token、/.well-known/jwks 是明文 HTTP，不关掉会被 ID2083 拒掉。容器不映射端口，外部够不着。
                if (!environment.IsDevelopment())
                    aspNetCore.DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        return services;
    }

    private static RateLimitPartition<string> CreateIpLimiter(
        HttpContext context,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                AutoReplenishment = true,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
}
