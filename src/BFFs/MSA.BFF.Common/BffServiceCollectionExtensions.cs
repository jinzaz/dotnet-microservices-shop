using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MSA.BFF.Common.Auth;
using MSA.BFF.Common.Middleware;
using MSA.Common;
using MSA.Shared;
using OpenIddict.Abstractions;
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

        // 授权/登出端点要留给浏览器跳转，必须是公网域名；换 token 和拉 JWKS 是后端对后端，容器网络内部直连即可，
        // 没必要经入口代理绕一圈。必填：appsettings.json 本地默认值和 Docker 的环境变量都要写，漏配直接启动失败，
        // 不悄悄退回成 Issuer——那样配置漏了也看不出来，等于白改。
        var internalValue = configuration["OpenIddict:InternalAddress"];
        if (!Uri.TryCreate(internalValue, UriKind.Absolute, out var internalBase) ||
            internalBase.Scheme != Uri.UriSchemeHttp && internalBase.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "OpenIddict:InternalAddress must be an absolute HTTP or HTTPS URI.");
        }
        internalBase = new Uri(internalBase.AbsoluteUri.TrimEnd('/') + "/");

        var clientId = configuration["OpenIddict:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("OpenIddict:ClientId is required.");

        // 客户端密钥只读环境变量，不从配置读取：与认证中心注册客户端用的是同一个变量。
        var clientSecret = MsaEnvironment.ClientSecret(clientId);
        if (clientSecret is null)
        {
            throw new InvalidOperationException(
                $"BFF client secret is missing. Set {MsaEnvironment.ClientSecretVariable(clientId)} in the environment or in msa.env.");
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

                // 不能手写静态 OpenIddictConfiguration：那样 OpenIddict 不会去拉 JWKS，id_token 验签必失败。
                // 改为从内部地址拉 discovery（签名密钥、*Supported 元数据都来自它），再改写端点：认证中心按请求进来的
                // 地址生成端点，内部拉到的全是内部地址，所以浏览器要跳的授权/登出端点改回公网，token/JWKS 走内部。
                options.AddEventHandler<OpenIddictClientEvents.HandleConfigurationResponseContext>(handler => handler
                    .UseInlineHandler(context =>
                    {
                        context.Configuration.AuthorizationEndpoint = new Uri(issuer, "connect/authorize");
                        context.Configuration.EndSessionEndpoint = new Uri(issuer, "connect/logout");
                        context.Configuration.TokenEndpoint = new Uri(internalBase, "connect/token");
                        context.Configuration.JsonWebKeySetUri = new Uri(internalBase, ".well-known/jwks");
                        return default;
                    })
                    .SetOrder(int.MaxValue)
                    .SetType(OpenIddictClientHandlerType.Custom));

                var registration = new OpenIddictClientRegistration
                {
                    Issuer = issuer,
                    ConfigurationEndpoint = new Uri(internalBase, ".well-known/openid-configuration"),
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
