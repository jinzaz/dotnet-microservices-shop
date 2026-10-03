using MSA.Common;
using MSA.Observability;
using FluentValidation;
using MSA.Grpc;
using MSA.Redis;
using MSA.InventoryService.Grpc;
using MSA.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.InventoryService.Application;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Endpoints;
using MSA.InventoryService.Infrastructure;
using MSA.InventoryService.Integration;
using MSA.InventoryService.Middleware;
using System.Text.Json.Serialization;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddMsaServiceDefaults();
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

// 提交后失效：库存与配额的每次落库都在事务提交之后清掉读模型缓存，并通知秒杀余量订阅者。
builder.Services.AddMsaRedis(builder.Configuration, "inventory");
builder.Services.AddMsaCacheInvalidation(InventoryCacheKeys.Configure);
builder.Services.AddDbContext<InventoryDbContext>((services, options) => options
    .UseNpgsql(postgresql)
    .UseMsaCacheInvalidation(services));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<InventoryDbContext>());
builder.Services.AddScoped<IInventoryTransactionLock, PostgresInventoryTransactionLock>();
builder.Services.AddScoped<InventoryReadModel>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<AddStockCommand>());
builder.Services.AddValidatorsFromAssemblyContaining<AddStockCommand>();
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

builder.Services.AddOptions<OfferStreamOptions>()
    .Bind(builder.Configuration.GetSection(OfferStreamOptions.SectionName))
    .Validate(options => options.CoalesceWindow >= TimeSpan.Zero && options.RefreshInterval > TimeSpan.Zero,
        "OfferStream timings must be positive.")
    .ValidateOnStart();
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddMsaGrpcServer(builder.Environment.IsDevelopment());
builder.Services.AddOutbox<InventoryDbContext>();
// 兜底释放到期未支付的预占，避免释放消息丢失时库存被永久锁死。
builder.Services.AddHostedService<InventoryReservationExpiryService>();
// Outbox/Inbox 保留期清理，并为运维接口暴露失败消息与卡住的 Saga。
builder.Services.AddMessagingOperations<InventoryDbContext>();
builder.Services.AddIntegrationEventHandlersFromAssemblyContaining<AddStockCommand>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetMsaIssuer(builder.Configuration);
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
builder.Services.AddHealthChecks()
    .AddCheck<InventoryDatabaseHealthCheck>("postgresql", tags: ["ready"]);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

app.UseMsaServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapMsaGrpcService<InventoryAvailabilityGrpcService>("inventory-api");
app.MapInventoryEndpoints();
app.MapInventoryAdminEndpoints();
app.MapMessagingAdminEndpoints("inventory", "inventory.admin.read", "inventory.admin.write");
app.MapInventoryAllocationEndpoints();
app.MapFlashSaleEndpoints();
app.MapFlashSaleAdminEndpoints();

app.Run();
