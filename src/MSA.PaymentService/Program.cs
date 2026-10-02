using MSA.Common;
using MSA.Observability;
using MSA.Redis;
using Essensoft.Paylinks.Alipay.Client.Extensions;
using MSA.Grpc;
using MSA.Grpc.Contracts.Order;
using MSA.PaymentService.Application.Realtime;
using MSA.PaymentService.Grpc;
using MSA.PaymentService.Infrastructure.Grpc;
using MSA.Repositories.Persistence;
using Essensoft.Paylinks.WeChatPay.Client.Extensions;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Endpoints;
using MSA.PaymentService.Infrastructure;
using MSA.PaymentService.Integration;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
// 基础设施地址、OTLP 与 gRPC 下游地址、客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);
var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddMsaRedis(builder.Configuration, "payment");
builder.Services.AddMsaCacheInvalidation(PaymentCacheKeys.Configure);
builder.Services.AddDbContext<PaymentDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .UseMsaCacheInvalidation(services));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<PaymentDbContext>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<PaymentProcessingOptions>()
    .Bind(builder.Configuration.GetSection("PaymentProcessing"))
    .Validate(options => options.IsValid(), "Payment processing options are invalid.")
    .ValidateOnStart();
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<ConfirmPaymentCommand>());
builder.Services.AddMsaApplicationPipeline();
builder.Services.Configure<PaylinksOptions>(builder.Configuration.GetSection("Paylinks"));
builder.Services.Configure<PaymentEndpointOptions>(builder.Configuration.GetSection("PaymentEndpoints"));
builder.Services.AddAlipayClient();
builder.Services.AddWeChatPayClient();
builder.Services.AddScoped<IPaymentGateway, AlipayGateway>();
builder.Services.AddScoped<IPaymentGateway, WeChatPayGateway>();
builder.Services.AddScoped<IPaymentNotificationVerifier, AlipayNotificationVerifier>();
builder.Services.AddScoped<IPaymentNotificationVerifier, WeChatPayNotificationVerifier>();
builder.Services.AddScoped<PaymentGatewayResolver>();
builder.Services.AddScoped<PaymentProviderOperationStore>();
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddMsaGrpcServer(builder.Environment.IsDevelopment());
// 建单收款前向订单核对金额与定价快照：事件是可信的，但可能已经过期。
builder.Services.AddMsaServiceAuthentication(builder.Configuration.GetSection(ServiceAuthenticationOptions.SectionName));
builder.Services.AddMsaGrpcClient<OrderQuery.OrderQueryClient>(builder.Configuration.GetSection("GrpcClients:Order"));
builder.Services.AddScoped<IOrderAuthority, OrderAuthorityGateway>();
builder.Services.AddScoped<OrderPaymentPrecheck>();
builder.Services.AddOutbox<PaymentDbContext>();
// Outbox/Inbox 保留期清理，并为运维接口暴露失败消息与卡住的 Saga。
builder.Services.AddMessagingOperations<PaymentDbContext>();
builder.Services.AddIntegrationEventHandler<PaymentRequestedIntegrationEvent, PaymentRequestedHandler>();
builder.Services.AddIntegrationEventHandler<CancelPaymentRequestedIntegrationEvent, CancelPaymentRequestedHandler>();
builder.Services.AddIntegrationEventHandler<RefundPaymentRequestedIntegrationEvent, RefundPaymentRequestedHandler>();
builder.Services.AddHostedService<PaymentExpiryService>();
builder.Services.AddHostedService<PaymentProviderOperationDispatcher>();
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("payment-api", "payment.admin.write", "payment.admin.read");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "payment-api",
    "payment.admin.read",
    "payment.admin.write");
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapMsaGrpcService<PaymentQueryGrpcService>("payment-api");
app.MapPaymentApiEndpoints();
app.MapPaymentAdminEndpoints();
app.MapMessagingAdminEndpoints("payment", "payment.admin.read", "payment.admin.write");
app.MapPaymentCallbackEndpoints();

app.Run();
