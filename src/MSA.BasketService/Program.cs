using FluentValidation;
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
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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

builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.VirtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
    options.ExchangeName = builder.Configuration["RabbitMQ:ExchangeName"] ?? "msa.commerce.events";
    options.QueueName = builder.Configuration["RabbitMQ:QueueName"] ?? "msa.basket";
    options.DeadLetterExchange = builder.Configuration["RabbitMQ:DeadLetterExchange"] ?? $"{options.ExchangeName}.dlx";
    options.DeadLetterQueue = builder.Configuration["RabbitMQ:DeadLetterQueue"] ?? $"{options.QueueName}.dlq";
    options.PrefetchCount = builder.Configuration.GetValue<ushort?>("RabbitMQ:PrefetchCount") ?? 32;
});
builder.Services.AddOutbox<BasketDbContext>();
builder.Services.AddIntegrationEventHandler<ProductChangedIntegrationEvent, ProductChangedHandler>();
builder.Services.AddIntegrationEventHandler<OrderSubmittedIntegrationEvent, OrderSubmittedHandler>();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("basket-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthorization();

builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<BasketApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<BasketDatabaseHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());

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
app.Run();
