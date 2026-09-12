using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.NotificationService.Infrastructure;
using MSA.NotificationService.Integration;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<NotificationDbContext>());
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<OrderConfirmedHandler>());
builder.Services.AddMsaApplicationPipeline();
builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.ExchangeName = builder.Configuration["RabbitMQ:ExchangeName"] ?? "msa.commerce.events";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.VirtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.QueueName = "msa.notification";
    options.DeadLetterExchange = $"{options.ExchangeName}.dlx";
    options.DeadLetterQueue = $"{options.QueueName}.dlq";
});
builder.Services.AddIntegrationEventHandler<OrderConfirmedIntegrationEvent, OrderConfirmedHandler>();
builder.Services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, OrderCancelledHandler>();
builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
builder.Services.AddHostedService<NotificationDispatchService>();
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("notification-api", "notification.admin.write", "notification.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapGet("/api/notifications/{userId}", async (
    string userId,
    int skip,
    int take,
    ClaimsPrincipal user,
    NotificationDbContext db,
    CancellationToken ct) =>
{
    var scopes = user.Claims.Where(x => x.Type == "scope").SelectMany(x => x.Value.Split(' '));
    if (user.FindFirstValue("sub") != userId && !scopes.Contains("notifications.read.all"))
        return Results.Forbid();

    var rows = await db.Notifications.AsNoTracking()
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedOnUtc)
        .Skip(Math.Max(0, skip))
        .Take(Math.Clamp(take, 1, 100))
        .Select(x => new { x.Id, x.Subject, x.Body, x.Status, x.CreatedOnUtc, x.SentOnUtc })
        .ToListAsync(ct);
    return Results.Ok(rows);
}).RequireAuthorization();

app.Run();
