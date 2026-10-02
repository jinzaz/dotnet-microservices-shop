using MSA.Common;
using MSA.Observability;
using FluentValidation;
using MSA.Grpc;
using MSA.Redis;
using MSA.CatalogService.Grpc;
using MSA.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Endpoints;
using MSA.CatalogService.Infrastructure;
using MSA.CatalogService.Middleware;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using OpenIddict.Validation.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
// 基础设施地址、OTLP 与 gRPC 下游地址、客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);
var postgresql = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(postgresql))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

// 商品详情、列表、价格确认走 Cache Aside；商品、规格、图片、活动一落库，提交后按规则失效。
builder.Services.AddMsaRedis(builder.Configuration, "catalog");
builder.Services.AddMsaCacheInvalidation(CatalogCacheKeys.Configure);
builder.Services.AddDbContext<CatalogDbContext>((services, options) => options
    .UseNpgsql(postgresql)
    .UseMsaCacheInvalidation(services));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<CatalogDbContext>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateDraftProductCommand>());
// 抢券闸门要包在事务外面：抢光标记在提交之后写，在途锁覆盖整个事务。所以先于应用管道注册。
builder.Services.AddTransient<IPipelineBehavior<ClaimCouponCommand, CouponClaimResultDto>, CouponClaimGate>();
builder.Services.AddMsaApplicationPipeline();

builder.Services.AddScoped<CatalogStore>();
builder.Services.AddScoped<ICategoryRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<IProductRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<IPromotionRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<ICouponRepository>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<ICatalogReadService>(services => services.GetRequiredService<CatalogStore>());
builder.Services.AddScoped<CouponCampaignStore>();
builder.Services.AddScoped<ICouponCampaignRepository>(services => services.GetRequiredService<CouponCampaignStore>());
builder.Services.AddScoped<ICouponCampaignReadService>(services => services.GetRequiredService<CouponCampaignStore>());
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
builder.Services.AddScoped<IValidator<CreateCouponBatchCommand>, CreateCouponBatchValidator>();
builder.Services.AddScoped<IValidator<CreateCouponCampaignCommand>, CreateCouponCampaignValidator>();
builder.Services.AddScoped<IValidator<ReviseCouponCampaignCommand>, ReviseCouponCampaignValidator>();

builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddOutbox<CatalogDbContext>();
// Outbox/Inbox 保留期清理，并为运维接口暴露失败消息与卡住的 Saga。
builder.Services.AddMessagingOperations<CatalogDbContext>();
builder.Services.AddExceptionHandler<CatalogApiExceptionHandler>();
builder.Services.AddProblemDetails();
// BM 以名称传枚举（如 ProductMediaRequest.MediaType = "Image"），默认序列化只接受数字。
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
// 服务间实时调用：同一套 OpenIddict scope / resource，策略与 REST 一致。
builder.Services.AddMsaGrpcServer(builder.Environment.IsDevelopment());

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

// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

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
app.MapMsaGrpcService<CatalogPricingGrpcService>("catalog-api");
app.MapMsaGrpcService<CouponQueryGrpcService>("catalog-api");
app.MapMessagingAdminEndpoints("catalog", "catalog.admin.read", "catalog.admin.write");
app.Run();
