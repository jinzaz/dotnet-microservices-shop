using MSA.Common;
using MSA.Observability;
using MSA.Redis;
using FluentValidation;
using MSA.Grpc;
using MSA.Grpc.Contracts.Catalog;
using MSA.Grpc.Contracts.Inventory;
using MSA.Grpc.Contracts.Payment;
using MSA.Grpc.Contracts.User;
using MSA.OrderService.Application.Realtime;
using MSA.OrderService.Grpc;
using MSA.OrderService.Infrastructure.Grpc;
using MSA.Repositories.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.OrderService.Application;
using MSA.OrderService.Application.CancelOrder;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.ManageFulfillment;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.OrderService.Integration;
using MSA.OrderService.Middleware;
using MSA.Saga.Extensions;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
// Redis / PostgreSQL / RabbitMQ / OSS 地址与客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<OrderDatabaseHealthCheck>("postgresql", tags: ["ready"]);

var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");
// 生效促销整份缓存；促销投影一落库（集成事件处理器里），提交后失效。
builder.Services.AddMsaRedis(builder.Configuration, "order");
builder.Services.AddMsaCacheInvalidation(rules => rules
    .On<PromotionProjection>((_, change) => change.Key(OrderPricingService.ActivePromotionsKey)));
builder.Services.AddDbContext<OrderDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .UseMsaCacheInvalidation(services));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<OrderDbContext>());
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderSubmissionLock, PostgresOrderSubmissionLock>();
builder.Services.AddScoped<IOrderFulfillmentLock, PostgresOrderFulfillmentLock>();
builder.Services.AddScoped<IOrderPricingService, OrderPricingService>();
builder.Services.AddScoped<ICouponRedemptionStore, PostgresCouponRedemptionStore>();
builder.Services.AddScoped<IPromotionRuleEvaluator, MinimumOrderAmountRule>();
builder.Services.AddScoped<IPromotionRuleEvaluator, MinimumItemQuantityRule>();
builder.Services.AddScoped<IPromotionBenefitCalculator, PercentageOffBenefit>();
builder.Services.AddScoped<IPromotionBenefitCalculator, FixedAmountOffBenefit>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OrderCheckoutOptions>()
    .Bind(builder.Configuration.GetSection(OrderCheckoutOptions.SectionName))
    .Validate(options => options.PaymentWindow >= TimeSpan.FromMinutes(1), "PaymentWindow must be at least one minute.")
    .Validate(options => options.PaymentWindow <= TimeSpan.FromHours(24), "PaymentWindow cannot exceed 24 hours.")
    .ValidateOnStart();

builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssemblyContaining<CreateOrderCommand>());
builder.Services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
builder.Services.AddScoped<IValidator<UpdateOrderFulfillmentCommand>, UpdateOrderFulfillmentCommandValidator>();
builder.Services.AddMsaApplicationPipeline();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("OrderSubmission", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            httpContext.User.FindFirstValue("sub")
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,
                TokensPerPeriod = 2,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = true,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
// 服务间实时调用：机器身份走 OpenIddict client_credentials，scope 沿用现有的 catalog / inventory / payment。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaCatalogPriceAuthority(builder.Configuration.GetSection("GrpcClients:Catalog"));
// 券的确认与价格确认同一个目录端点，复用同一段配置（地址、scope、超时）。
builder.Services.AddMsaGrpcClient<CouponQuery.CouponQueryClient>(builder.Configuration.GetSection("GrpcClients:Catalog"));
builder.Services.AddMsaGrpcClient<InventoryAvailability.InventoryAvailabilityClient>(builder.Configuration.GetSection("GrpcClients:Inventory"));
builder.Services.AddMsaGrpcClient<PaymentQuery.PaymentQueryClient>(builder.Configuration.GetSection("GrpcClients:Payment"));
builder.Services.AddMsaGrpcClient<UserAddressQuery.UserAddressQueryClient>(builder.Configuration.GetSection("GrpcClients:User"));
builder.Services.AddScoped<IInventoryAvailabilityGateway, InventoryAvailabilityGateway>();
builder.Services.AddScoped<IPaymentStatusGateway, PaymentStatusGateway>();
builder.Services.AddScoped<ICouponAuthority, CouponAuthorityGateway>();
builder.Services.AddScoped<IUserAddressAuthority, UserAddressAuthorityGateway>();
// 订单自己也对外提供实时快照（支付核对金额、后台聚合详情）。
builder.Services.AddMsaGrpcServer(builder.Environment.IsDevelopment());

builder.Services.AddOutbox<OrderDbContext>();
// Outbox/Inbox 保留期清理，并为运维接口暴露失败消息与卡住的 Saga。
builder.Services.AddMessagingOperations<OrderDbContext>();
builder.Services.AddSaga<OrderDbContext>();
builder.Services.AddIntegrationEventHandler<ProductChangedIntegrationEvent, ProductChangedHandler>();
builder.Services.AddIntegrationEventHandler<ProductVariantChangedIntegrationEvent, ProductVariantChangedHandler>();
builder.Services.AddIntegrationEventHandler<PromotionSnapshotChangedIntegrationEvent, PromotionSnapshotChangedHandler>();
builder.Services.AddIntegrationEventHandler<CouponSnapshotChangedIntegrationEvent, CouponSnapshotChangedHandler>();
builder.Services.AddIntegrationEventHandler<FlashSaleCampaignOpenedIntegrationEvent, FlashSaleCampaignOpenedHandler>();
builder.Services.AddIntegrationEventHandler<FlashSaleCampaignClosedIntegrationEvent, FlashSaleCampaignClosedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryAllocationOpenedIntegrationEvent, InventoryAllocationOpenedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryAllocationClosedIntegrationEvent, InventoryAllocationClosedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryReservedIntegrationEvent, InventoryReservedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryRejectedIntegrationEvent, InventoryRejectedHandler>();
builder.Services.AddIntegrationEventHandler<PaymentSucceededIntegrationEvent, PaymentSucceededHandler>();
builder.Services.AddIntegrationEventHandler<PaymentFailedIntegrationEvent, PaymentFailedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryCommittedIntegrationEvent, InventoryCommittedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryReleasedIntegrationEvent, InventoryReleasedHandler>();
builder.Services.AddIntegrationEventHandler<PaymentCancelledIntegrationEvent, PaymentCancelledHandler>();
builder.Services.AddIntegrationEventHandler<PaymentRefundedIntegrationEvent, PaymentRefundedHandler>();
builder.Services.AddIntegrationEventHandler<PaymentRefundFailedIntegrationEvent, PaymentRefundFailedHandler>();
builder.Services.AddScoped<OrderCancellationWorkflow>();
builder.Services.AddScoped<ISagaTimeoutHandler, OrderSagaTimeoutHandler>();


// 如果注入了 AddControllers() , 则必须手动指定认证方案，否则会出现 409 Conflict 错误。因为 AddControllers() 会覆盖默认的认证方案。
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("order-api", "order.admin.write", "order.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});

builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "order-api",
    "order.admin.read",
    "order.admin.write");

// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapMessagingAdminEndpoints("order", "order.admin.read", "order.admin.write");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapControllers();
app.MapMsaGrpcService<OrderQueryGrpcService>("order-api");

app.Run();
