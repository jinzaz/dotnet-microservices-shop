using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.InventoryService.Application;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Endpoints;
using MSA.InventoryService.Infrastructure;
using MSA.InventoryService.Integration;
using MSA.InventoryService.Middleware;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text.Json.Serialization;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(postgresql));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<InventoryDbContext>());
builder.Services.AddScoped<IInventoryTransactionLock, PostgresInventoryTransactionLock>();
builder.Services.AddScoped<InventoryCacheInvalidator>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<AddStockCommand>());
builder.Services.AddScoped<IValidator<AddStockCommand>, AddStockValidator>();
builder.Services.AddScoped<IValidator<AdjustStockCommand>, AdjustStockCommandValidator>();
builder.Services.AddScoped<IValidator<CreateInventoryAllocationCommand>, CreateInventoryAllocationValidator>();
// Must wrap TransactionBehavior: read-model eviction is only safe after the command commits.
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InventoryCacheInvalidationBehavior<,>));
builder.Services.AddMsaApplicationPipeline();

builder.Services.AddOptions<InventoryReservationOptions>()
    .Bind(builder.Configuration.GetSection(InventoryReservationOptions.SectionName))
    .Validate(options => options.RegularLifetime > TimeSpan.Zero, "RegularLifetime must be positive.")
    .Validate(options => options.OfferLifetime > TimeSpan.Zero, "OfferLifetime must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<InventoryAllocationOptions>()
    .Bind(builder.Configuration.GetSection(InventoryAllocationOptions.SectionName))
    .Validate(options => options.AvailabilityCacheDuration > TimeSpan.Zero, "AvailabilityCacheDuration must be positive.")
    .Validate(options => options.ExpiryScanInterval > TimeSpan.Zero, "ExpiryScanInterval must be positive.")
    .Validate(options => options.ExpiryBatchSize > 0, "ExpiryBatchSize must be positive.")
    .ValidateOnStart();
builder.Services.AddHostedService<InventoryAllocationExpiryService>();

builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.ExchangeName = builder.Configuration["RabbitMQ:ExchangeName"] ?? "msa.commerce.events";
    options.VirtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.QueueName = "msa.inventory";
    options.DeadLetterExchange = $"{options.ExchangeName}.dlx";
    options.DeadLetterQueue = $"{options.QueueName}.dlq";
    options.PrefetchCount = builder.Configuration.GetValue<ushort?>("RabbitMQ:PrefetchCount") ?? 64;
});
builder.Services.AddOutbox<InventoryDbContext>();
builder.Services.AddIntegrationEventHandler<OrderSubmittedIntegrationEvent, OrderSubmittedHandler>();
builder.Services.AddIntegrationEventHandler<ReleaseInventoryIntegrationEvent, ReleaseInventoryHandler>();
builder.Services.AddIntegrationEventHandler<CommitInventoryIntegrationEvent, CommitInventoryHandler>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("inventory-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "inventory-api",
    "inventory.admin.read",
    "inventory.admin.write");

builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<InventoryDatabaseHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapInventoryEndpoints();
app.MapInventoryAdminEndpoints();
app.MapInventoryAllocationEndpoints();
app.MapFlashSaleEndpoints();
app.MapFlashSaleAdminEndpoints();

app.Run();
