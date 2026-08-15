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

// RabbitMQ/EventBus registration via messaging extension (will register SagaCoordinator, repositories and eventbus)
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddMessagingWithSaga(opts => { opts.HostName = rabbitHost; opts.Exchange = "events"; opts.Port = 5672; });

// register example saga handler (implemented in MSA.Messaging)
builder.Services.AddSingleton<ISagaHandler, MSA.Libraries.Messaging.Handlers.OrderCreationSagaHandler>();

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
