using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Endpoints;
using MSA.CatalogService.Infrastructure;
using MSA.CatalogService.Middleware;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

var redis = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

builder.Services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(postgresql));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<CatalogDbContext>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateDraftProductCommand>());
builder.Services.AddMsaApplicationPipeline();

builder.Services.AddScoped<CatalogStore>();
builder.Services.AddScoped<ICategoryRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<IProductRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<IPromotionRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<ICouponRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<ICatalogReadService>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<ICatalogSnapshotPublisher, OutboxCatalogSnapshotPublisher>();
builder.Services.AddScoped<ICatalogAuditWriter, CatalogAuditWriter>();

builder.Services.AddScoped<IValidator<CreateCategoryAdminCommand>, CreateCategoryAdminValidator>();
builder.Services.AddScoped<IValidator<CreateDraftProductCommand>, CreateDraftProductValidator>();
builder.Services.AddScoped<IValidator<ChangeProductPriceCommand>, ChangeProductPriceValidator>();
builder.Services.AddScoped<IValidator<CreateProductVariantCommand>, CreateProductVariantValidator>();
builder.Services.AddScoped<IValidator<UpdateProductVariantCommand>, UpdateProductVariantValidator>();
builder.Services.AddScoped<IValidator<AddProductMediaCommand>, ProductMediaCommandValidator>();
builder.Services.AddScoped<IValidator<UpdateProductMediaCommand>, UpdateProductMediaValidator>();
builder.Services.AddScoped<IValidator<CreatePromotionCommand>, CreatePromotionValidator>();
builder.Services.AddScoped<IValidator<ReplacePromotionDefinitionCommand>, ReplacePromotionDefinitionValidator>();
builder.Services.AddScoped<IValidator<CreateCouponCommand>, CreateCouponValidator>();

builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redis);
builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.VirtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
    options.ExchangeName = builder.Configuration["RabbitMQ:ExchangeName"] ?? "msa.commerce.events";
    options.DeadLetterExchange = builder.Configuration["RabbitMQ:DeadLetterExchange"] ?? "msa.commerce.events.dlx";
    options.QueueName = builder.Configuration["RabbitMQ:QueueName"] ?? "msa.catalog";
    options.DeadLetterQueue = builder.Configuration["RabbitMQ:DeadLetterQueue"] ?? "msa.catalog.dlq";
});
builder.Services.AddOutbox<CatalogDbContext>();
builder.Services.AddExceptionHandler<CatalogApiExceptionHandler>();
builder.Services.AddProblemDetails();
// BM 以名称传枚举（如 ProductMediaRequest.MediaType = "Image"），默认序列化只接受数字。
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();

builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("catalog-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "catalog-api",
    "catalog.admin.read",
    "catalog.admin.write");

builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapCatalogEndpoints();
app.MapCatalogAdminEndpoints();
app.Run();
