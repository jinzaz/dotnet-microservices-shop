using Microsoft.AspNetCore.RateLimiting;
using Polly;
using Polly.Timeout;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http;
using MSA.ApiGateway;
// OpenTelemetry removed from inline initialization to avoid package resolution issues here

using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Consul;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// OpenTelemetry tracing: 请在有可用 OpenTelemetry 包时启用并配置导出器（Console/OTLP 等）。

// Rate limiting (token bucket)
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("Global", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromSeconds(1),
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
        options.SetIssuer("https://auth.example.com/");
        options.AddAudiences("api");
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

// Use YARP and load static config; dynamic service discovery can be plugged in separately
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// OpenTelemetry tracing
builder.Services.AddOpenTelemetryTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.ApiGateway";
    otelBuilder
        .SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService(svcName))
        .AddAspNetCoreInstrumentation();

    // http client instrumentation if available
    try { otelBuilder.AddHttpClientInstrumentation(); } catch { }

    var otlp = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    var enableConsole = bool.TryParse(builder.Configuration["OpenTelemetry:EnableConsoleExporter"], out var b) && b;
    if (!string.IsNullOrEmpty(otlp))
    {
        otelBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    }
    if (enableConsole)
    {
        otelBuilder.AddConsoleExporter();
    }
});

// Consul client & dynamic YARP config provider
builder.Services.AddSingleton<IConsulClient>(sp => new ConsulClient(cfg =>
{
    var address = builder.Configuration["Consul:Address"] ?? "http://localhost:8500";
    cfg.Address = new Uri(address);
}));

builder.Services.AddSingleton<ConsulProxyConfigProvider>();
builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(sp => sp.GetRequiredService<ConsulProxyConfigProvider>());
builder.Services.AddHostedService<ConsulRefreshHostedService>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapReverseProxy();

app.Run();
