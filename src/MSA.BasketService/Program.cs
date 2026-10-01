using MSA.Observability;
using FluentValidation;
using MSA.Repositories.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Application;
using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Domain;
using MSA.BasketService.Endpoints;
using MSA.BasketService.Infrastructure;
using MSA.BasketService.Integration;
using MSA.BasketService.Middleware;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.EventBusOutbox.Extensions;
using MSA.Grpc;
using MSA.Grpc.Contracts.Catalog;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<BasketDbContext>(options => options.UseNpgsql(postgresql));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<BasketDbContext>());
builder.Services.AddScoped<BasketStore>();
builder.Services.AddScoped<IBasketRepository>(services => services.GetRequiredService<BasketStore>());
builder.Services.AddScoped<IBasketProductCatalog>(services => services.GetRequiredService<BasketStore>());
builder.Services.AddScoped<IBasketReadService>(services => services.GetRequiredService<BasketStore>());
builder.Services.AddScoped<IBasketMutationLock, PostgresBasketMutationLock>();
builder.Services.AddSingleton(TimeProvider.System);
// 结算页的价格以目录此刻为准：机器身份走 client_credentials，scope 沿用现有的 catalog。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaCatalogPriceAuthority(builder.Configuration.GetSection("GrpcClients:Catalog"));

builder.Services.AddOptions<BasketOptions>()
    .Bind(builder.Configuration.GetSection(BasketOptions.SectionName))
    .Validate(
        options => options.MaximumLineQuantity is >= 1 and <= BasketLimits.MaximumSupportedLineQuantity,
        $"MaximumLineQuantity must be 1-{BasketLimits.MaximumSupportedLineQuantity}.")
    .Validate(
        options => options.MaximumDistinctItems is >= 1 and <= BasketLimits.MaximumSupportedDistinctItems,
        $"MaximumDistinctItems must be 1-{BasketLimits.MaximumSupportedDistinctItems}.")
    .Validate(
        options => options.MaximumCouponCodes is >= 0 and <= BasketLimits.MaximumSupportedCouponCodes,
        $"MaximumCouponCodes must be 0-{BasketLimits.MaximumSupportedCouponCodes}.")
    .ValidateOnStart();

builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<GetBasketQuery>());
builder.Services.AddScoped<IValidator<AddBasketItemCommand>, AddBasketItemCommandValidator>();
builder.Services.AddScoped<IValidator<SetBasketItemQuantityCommand>, SetBasketItemQuantityCommandValidator>();
builder.Services.AddScoped<IValidator<RemoveBasketItemCommand>, RemoveBasketItemCommandValidator>();
builder.Services.AddScoped<IValidator<ClearBasketCommand>, ClearBasketCommandValidator>();
builder.Services.AddScoped<IValidator<ReplaceBasketCouponsCommand>, ReplaceBasketCouponsCommandValidator>();
builder.Services.AddMsaApplicationPipeline();

builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddOutbox<BasketDbContext>();
// Outbox/Inbox 保留期清理，并为运维接口暴露失败消息与卡住的 Saga。
builder.Services.AddMessagingOperations<BasketDbContext>();
builder.Services.AddIntegrationEventHandler<ProductChangedIntegrationEvent, ProductChangedHandler>();
builder.Services.AddIntegrationEventHandler<ProductVariantChangedIntegrationEvent, ProductVariantChangedHandler>();
builder.Services.AddIntegrationEventHandler<OrderSubmittedIntegrationEvent, OrderSubmittedHandler>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("basket-api", "basket.admin.write", "basket.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "basket-api",
    "basket.admin.read",
    "basket.admin.write");

builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<BasketApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<BasketDatabaseHealthCheck>("postgresql", tags: ["ready"]);
// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BasketDbContext>().Database.MigrateAsync();
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
app.MapBasketEndpoints();
app.MapMessagingAdminEndpoints("basket", "basket.admin.read", "basket.admin.write");
app.Run();
