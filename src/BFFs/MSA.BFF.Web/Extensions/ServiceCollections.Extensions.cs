using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using MSA.BFF.Web.Auth;
using MSA.BFF.Web.Auth.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using Yarp.ReverseProxy.Transforms;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.BFF.Web.Extensions;

public static class ServiceCollections
{
    internal const string BffCookie = "BffCookie";

    public static IServiceCollection AddBffServicesDefault(
        this IServiceCollection services, string redisConnectionString)
    {
        services.AddScoped<IUserAccessTokenProvider, UserAccessTokenProvider>();
        services.AddSingleton<RedisTicketStore>();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "MsaWebBff:";
        });
        services.AddAuthentication(BffCookie)
        .AddCookie(BffCookie, options => {
            options.Cookie.Name = "__Host-MSAWebSession";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;
            options.Cookie.SameSite =
                SameSiteMode.Lax;
            options.ExpireTimeSpan =
                   TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/api/bff/login";
            options.LogoutPath = "/api/bff/logout";

            //
            // API 不要 302 去登录页。
            //

            options.Events.OnRedirectToLogin =
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes
                            .Status401Unauthorized;

                    return Task.CompletedTask;
                };

            options.Events.OnRedirectToAccessDenied =
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status403Forbidden;

                    return Task.CompletedTask;
                };
        });

        services.AddAntiforgery(options => {
            options.HeaderName = "X-XSRF-TOKEN";

            options.Cookie.Name = "__Host-MSAWebSession.XSRF-TOKEN";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });

        services
            .AddOptions<CookieAuthenticationOptions>(BffCookie)
            .Configure<RedisTicketStore>((options, store) =>
            {
                options.SessionStore = store;
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddYarp(this IServiceCollection services, IConfigurationSection yarpConfiguration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(yarpConfiguration)
            .AddTransforms(context =>
            {
                context.AddRequestTransform(TransformRequestAsync);
            });

        return services;


        static async ValueTask TransformRequestAsync(
            RequestTransformContext context)
        {
            context.ProxyRequest.Headers
                .Remove("Cookie");
            // 不信任浏览器自己传进来的 Bearer。
            context.ProxyRequest.Headers
                .Remove("Authorization");
            var provider =
                context.HttpContext
                    .RequestServices
                    .GetRequiredService<
                        IUserAccessTokenProvider>();
            var token =
                await provider.GetAccessTokenAsync(
                    context.HttpContext,
                    context.HttpContext
                        .RequestAborted);
            if (!string.IsNullOrWhiteSpace(token))
            {
                context.ProxyRequest
                    .Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        token);
            }
        }
    }

    public static IServiceCollection AddOpenIddictDefault(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var issuerValue = configuration["OpenIddict:Issuer"];
        if (!Uri.TryCreate(issuerValue, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttp && issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("OpenIddict:Issuer must be an absolute HTTP or HTTPS URI.");
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

        services.AddOpenIddict()
            .AddCore(options => {
                options.UseEntityFrameworkCore()
                    .UseDbContext<BffWebDbContext>();

                options.UseQuartz();
            })
            .AddClient(options =>
            {
                options
                    .AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow();

                if (environment.IsDevelopment())
                {
                    options
                        .AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    var path = configuration["Certificates:Path"];
                    var password = configuration["Certificates:Password"];
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException("A production OpenIddict client certificate is required.");

                    var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
                    options
                        .AddEncryptionCertificate(certificate)
                        .AddSigningCertificate(certificate);
                }

                options
                    .UseAspNetCore()
                    .EnableStatusCodePagesIntegration()
                    .EnableRedirectionEndpointPassthrough()
                    .EnablePostLogoutRedirectionEndpointPassthrough();

                options.UseSystemNetHttp()
                    .SetProductInformation(typeof(Program).Assembly);

                options.AddRegistration(new OpenIddictClientRegistration
                {
                    Issuer = issuer,
                    RegistrationId = configuration["OpenIddict:RegistrationId"] ?? clientId,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    CodeChallengeMethods = { CodeChallengeMethods.Sha256 },
                    Scopes = {
                        Scopes.OpenId,
                        Scopes.Email,
                        Scopes.Profile,
                        Scopes.Roles,
                        Scopes.OfflineAccess,
                        "user",
                        "catalog",
                        "inventory",
                        "notification",
                        "basket",
                        "order",
                        "payment"
                    },

                    // Note: to mitigate mix-up attacks, it's recommended to use a unique redirection endpoint
                    // URI per provider, unless all the registered providers support returning a special "iss"
                    // parameter containing their URL as part of authorization responses. For more information,
                    // see https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics#section-4.4.
                    RedirectUri = new Uri("api/bff/callback/login", UriKind.Relative),
                    PostLogoutRedirectUri = new Uri("api/bff/callback/logout", UriKind.Relative),
                });
            });
        return services;
    }
}
