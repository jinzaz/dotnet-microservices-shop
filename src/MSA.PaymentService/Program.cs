using Essensoft.Paylinks.Alipay.Client.Extensions;
using Essensoft.Paylinks.WeChatPay.Client.Extensions;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Endpoints;
using MSA.PaymentService.Infrastructure;
using MSA.PaymentService.Integration;
using OpenIddict.Validation.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(connectionString));
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
builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.ExchangeName = builder.Configuration["RabbitMQ:ExchangeName"] ?? "msa.commerce.events";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.VirtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
    options.QueueName = "msa.payment";
    options.DeadLetterExchange = $"{options.ExchangeName}.dlx";
    options.DeadLetterQueue = $"{options.QueueName}.dlq";
});
builder.Services.AddOutbox<PaymentDbContext>();
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
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(builder.Environment.ApplicationName))
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .AddOtlpExporter());

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
app.MapPaymentApiEndpoints();
app.MapPaymentAdminEndpoints();
app.MapPaymentCallbackEndpoints();

app.Run();
