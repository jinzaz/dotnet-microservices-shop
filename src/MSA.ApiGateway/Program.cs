using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Timeout;
using Yarp.ReverseProxy.Configuration;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http;
// OpenTelemetry removed from inline initialization to avoid package resolution issues here

using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using MSA.ApiGateway;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// OpenTelemetry tracing: 请在有可用 OpenTelemetry 包时启用并配置导出器（Console/OTLP 等）。

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

// Add OpenIddict validation (for downstream tokens)
builder.Services
    .AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(builder.Configuration["Authentication:Issuer"]
            ?? throw new InvalidOperationException("Authentication:Issuer is required."));
        options.AddAudiences(builder.Configuration["Authentication:Audience"] ?? "api");
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

// Use YARP and load static config; dynamic service discovery can be plugged in separately
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry().WithTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.ApiGateway";
    otelBuilder
        .SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService(svcName))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter();

    // optional: add HttpClient instrumentation and OTLP exporter if packages are available
    // to avoid compilation errors when specific exporter packages are not referenced, these are omitted here
    var enableConsole = bool.TryParse(builder.Configuration["OpenTelemetry:EnableConsoleExporter"], out var b) && b;
    if (enableConsole)
    {
        otelBuilder.AddConsoleExporter();
    }
});

// Consul client & dynamic YARP config provider
// Consul dynamic provider temporarily disabled; using static ReverseProxy config from appsettings.json

var app = builder.Build();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();

app.MapReverseProxy().RequireRateLimiting("Global");

app.Run();
