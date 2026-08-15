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
    builder.Services.AddDbContext<OutboxDbContext>(options =>
        options.UseNpgsql(outboxConn));
}
else
{
    // fallback to in-memory for development
    builder.Services.AddDbContext<OutboxDbContext>(options =>
        options.UseInMemoryDatabase("Outbox"));
}

builder.Services.AddScoped<IOutboxRepository, EfOutboxRepository>();

// RabbitMQ/EventBus registration
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddSingleton(new RabbitMqEventBus(rabbitHost));
builder.Services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<RabbitMqEventBus>());

// Outbox background sender
builder.Services.AddHostedService<OutboxSenderHostedService>();

var app = builder.Build();

// OpenTelemetry tracing
builder.Services.AddOpenTelemetry().WithTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.OrderService";
    otelBuilder.SetResourceBuilder(OpenTelemetry.Resources.ResourceBuilder.CreateDefault().AddService(svcName))
        .AddAspNetCoreInstrumentation();
    var enableConsole = bool.TryParse(builder.Configuration["OpenTelemetry:EnableConsoleExporter"], out var b) && b;
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
