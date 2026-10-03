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
// OTLP 地址来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);

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
        options.SetIssuer(builder.Configuration["Authentication:Issuer"]
            ?? throw new InvalidOperationException("Authentication:Issuer is required."));
        //options.AddAudiences(builder.Configuration["Authentication:Audience"] ?? "api"); // 网关暂时不参与授权，直接转发 downstream 的 access token，audience 由 downstream 验证。
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

// Use YARP and load static config; dynamic service discovery can be plugged in separately
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

// Consul client & dynamic YARP config provider
// Consul dynamic provider temporarily disabled; using static ReverseProxy config from appsettings.json

var app = builder.Build();

app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();

app.MapReverseProxy().RequireRateLimiting("Global");

app.Run();
