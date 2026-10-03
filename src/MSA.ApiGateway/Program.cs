using MSA.Common;
using MSA.Observability;
using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Timeout;
using Yarp.ReverseProxy.Configuration;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http;
using MSA.ApiGateway;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();

// Add services to the container.

// Rate limiting (token bucket)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("Global", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 200,
                TokensPerPeriod = 100,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Register Polly policies to be applied to HttpClients used by YARP via IHttpMessageHandlerBuilderFilter
builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter, PollyHttpMessageHandlerBuilderFilter>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.ForwardLimit = 1;

    //options.KnownIPNetworks.Add(
    //    new IPNetwork(
    //        IPAddress.Parse("172.30.0.0"),
    //        24));
});

// Add OpenIddict validation (for downstream tokens)

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services
    .AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetMsaIssuer(builder.Configuration);
        //options.AddAudiences(builder.Configuration["Authentication:Audience"] ?? "api"); // 网关暂时不参与授权，直接转发 downstream 的 access token，audience 由 downstream 验证。
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

// Use YARP and load static config; dynamic service discovery can be plugged in separately
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Consul client & dynamic YARP config provider
// Consul dynamic provider temporarily disabled; using static ReverseProxy config from appsettings.json

var app = builder.Build();

app.UseForwardedHeaders();
// UseMsaServiceDefaults 里的 HTTPS 重定向开关要在转发头处理之后调，否则它看到的还是没纠正过的 scheme。
app.UseMsaServiceDefaults();

app.UseRateLimiter();

app.UseAuthentication();

app.MapReverseProxy().RequireRateLimiting("Global");

app.Run();
