using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MSA.BFF.Common.Auth;
using MSA.BFF.Common.Middleware;
using MSA.Shared;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using Quartz;
using Yarp.ReverseProxy.Transforms;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.BFF.Common;

public static class BffServiceCollectionExtensions
{
    public static IServiceCollection AddBff(
        this IServiceCollection services,
        string redisConnectionString,
        string applicationName)
    {
        if (string.IsNullOrWhiteSpace(redisConnectionString))
            throw new ArgumentException("A Redis connection string is required.", nameof(redisConnectionString));
        if (string.IsNullOrWhiteSpace(applicationName) ||
            applicationName.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "The BFF application name must contain only ASCII letters and digits.",
                nameof(applicationName));
        }

        var application = new BffApplicationOptions(applicationName);
        services.AddSingleton(application);
        services.AddScoped<UserAccessTokenProvider>();
        services.AddSingleton<RedisTicketStore>();
        services.AddTransient<AntiforgeryValidationMiddleware>();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = application.RedisInstanceName;
        });

        services.AddAuthentication(BffDefaults.CookieScheme)
            .AddCookie(BffDefaults.CookieScheme, options =>
            {
                options.Cookie.Name = application.SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = $"{BffDefaults.EndpointPrefix}/login";
                options.LogoutPath = $"{BffDefaults.EndpointPrefix}/logout";
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = application.AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        services
            .AddOptions<CookieAuthenticationOptions>(BffDefaults.CookieScheme)
            .Configure<RedisTicketStore>((options, store) => options.SessionStore = store);

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddBffReverseProxy(
        this IServiceCollection services,
        IConfigurationSection proxyConfiguration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(proxyConfiguration)
            .AddTransforms(context => context.AddRequestTransform(TransformRequestAsync));

        return services;

        static async ValueTask TransformRequestAsync(RequestTransformContext context)
        {
            context.ProxyRequest.Headers.Remove("Cookie");
            context.ProxyRequest.Headers.Remove("Authorization");

            var token = await context.HttpContext.RequestServices
                .GetRequiredService<UserAccessTokenProvider>()
                .GetAccessTokenAsync(context.HttpContext, context.HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(token))
            {
                context.ProxyRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }
    }

    public static IServiceCollection AddBffOpenIddictClient<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        Assembly productAssembly)
        where TDbContext : DbContext
    {
        var issuerValue = configuration["OpenIddict:Issuer"];
        if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttp && issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "OpenIddict:Issuer must be an absolute HTTP or HTTPS URI.");
        }

        var clientId = configuration["OpenIddict:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("OpenIddict:ClientId is required.");

        var clientSecret = configuration["OpenIddict:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "OpenIddict:ClientSecret is required. Set it with OpenIddict__ClientSecret.");
        }

        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore().UseDbContext<TDbContext>();
                options.UseQuartz();
            })
            .AddClient(options =>
            {
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();

                if (environment.IsDevelopment())
                {
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // 签名 / 加密 OIDC state token 的证书：生产环境只从环境变量读取。
                    var certificate = OpenIddictCertificateEnvironment.Load();
                    options
                        .AddEncryptionCertificate(certificate)
                        .AddSigningCertificate(certificate);
                }

                options
                    .UseAspNetCore()
                    .EnableStatusCodePagesIntegration()
                    .EnableRedirectionEndpointPassthrough()
                    .EnablePostLogoutRedirectionEndpointPassthrough();

                options.UseSystemNetHttp().SetProductInformation(productAssembly);

                var registration = new OpenIddictClientRegistration
                {
                    Issuer = issuer,
                    RegistrationId = configuration["OpenIddict:RegistrationId"] ?? clientId,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    RedirectUri = ReadCallbackUri(
                        configuration,
                        "OpenIddict:RedirectUri",
                        "bff/callback/login"),
                    PostLogoutRedirectUri = ReadCallbackUri(
                        configuration,
                        "OpenIddict:PostLogoutRedirectUri",
                        "bff/callback/logout")
                };
                registration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                registration.Scopes.UnionWith([
                    Scopes.OpenId,
                    Scopes.Email,
                    Scopes.Profile,
                    Scopes.Roles,
                    Scopes.OfflineAccess]);
                registration.Scopes.UnionWith(configuration
                    .GetSection("OpenIddict:Scopes")
                    .Get<string[]>() ?? []);

                options.AddRegistration(registration);
            });

        return services;

        static Uri ReadCallbackUri(
            IConfiguration values,
            string key,
            string fallback)
        {
            var value = values[key] ?? fallback;
            if (!Uri.TryCreate(value, UriKind.Relative, out var uri) ||
                value.StartsWith('/') ||
                value.Contains('?') ||
                value.Contains('#'))
            {
                throw new InvalidOperationException(
                    $"{key} must be a relative path without a leading slash, query, or fragment.");
            }

            return uri;
        }
    }

    public static IApplicationBuilder UseBffAntiforgery(this IApplicationBuilder app) =>
        app.UseMiddleware<AntiforgeryValidationMiddleware>();
}
