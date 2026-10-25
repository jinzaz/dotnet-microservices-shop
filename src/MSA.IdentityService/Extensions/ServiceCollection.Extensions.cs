using MSA.Certificates;
using MSA.IdentityService.Infrastructure;
using MSA.IdentityService.Infrastructure.Captcha;
using MSA.IdentityService.Infrastructure.Throttling;
using MSA.Redis;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Validation.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

internal static class ServiceCollection
{
    internal static IServiceCollection AddIdentityThrottling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMsaRedis(configuration, "identity");
        services.AddOptions<ThrottleOptions>()
            .Bind(configuration.GetSection(ThrottleOptions.SectionName))
            .Validate(options => Enum.GetValues<ThrottlePolicy>()
                    .Select(options.For)
                    .All(rule => rule.PermitLimit > 0 && rule.WindowSeconds > 0),
                "Throttling rules need a positive PermitLimit and WindowSeconds.")
            .ValidateOnStart();
        services.AddSingleton<IdentityThrottle>();
        services.AddSingleton<CaptchaService>();
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
            // 短信登录即注册的账户没有邮箱；RequireUniqueEmail 会同时要求邮箱必填，所以改由 OptionalUniqueEmailValidator 只查唯一。
            options.User.RequireUniqueEmail = false;
            // 密码错误不锁账号（别人知道邮箱就能反复把它锁住），改为按"IP + 账号"计数，见 AuthController.PasswordLogin。
            options.SignIn.RequireConfirmedEmail = false;
        })
        .AddUserValidator<OptionalUniqueEmailValidator>()
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
                // 签名（oidc-signing）/ 加密（oidc-encryption）证书来自证书库，两个用途分开轮换，不用重启；
                // 证书库里还没有时，开发环境用开发证书，其余环境用环境变量里的 pfx（同一张兼作签名与加密）。
                if (environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();
                }
                options.UseMsaCertificates();

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
}
