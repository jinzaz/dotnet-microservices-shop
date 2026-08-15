using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Outbox/EF registration
var outboxConn = builder.Configuration.GetConnectionString("Postgresql");
if (!string.IsNullOrEmpty(outboxConn))
{
    builder.Services.AddDbContext<MSA.Libraries.Messaging.OutboxDbContext>(options =>
        options.UseNpgsql(outboxConn));
}
else
{
    // fallback to in-memory for development
    builder.Services.AddDbContext<MSA.Libraries.Messaging.OutboxDbContext>(options =>
        options.UseInMemoryDatabase("Outbox"));
}

builder.Services.AddScoped<MSA.Libraries.Messaging.IOutboxRepository, MSA.Libraries.Messaging.EfOutboxRepository>();

// RabbitMQ/EventBus registration
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddSingleton(new MSA.Libraries.Messaging.RabbitMqEventBus(rabbitHost));
builder.Services.AddSingleton<MSA.Libraries.Messaging.IEventBus>(sp => sp.GetRequiredService<MSA.Libraries.Messaging.RabbitMqEventBus>());

// Outbox background sender
builder.Services.AddHostedService<MSA.Libraries.Messaging.OutboxSenderHostedService>();

var app = builder.Build();

// OpenTelemetry tracing
builder.Services.AddOpenTelemetryTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.OrderService";
    otelBuilder.SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService(svcName))
        .AddAspNetCoreInstrumentation();
    try { otelBuilder.AddHttpClientInstrumentation(); } catch { }
    var otlp = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    var enableConsole = bool.TryParse(builder.Configuration["OpenTelemetry:EnableConsoleExporter"], out var b) && b;
    if (!string.IsNullOrEmpty(otlp)) otelBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
    if (enableConsole) otelBuilder.AddConsoleExporter();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
