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
using MSA.PaymentService.Infrastructure;
using MSA.PaymentService.Integration;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<PaymentDbContext>());
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
builder.Services.AddRabbitMQEventBus(options =>
{
    options.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
    options.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest";
    options.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
    options.QueueName = "msa.payment";
});
builder.Services.AddOutbox<PaymentDbContext>();
builder.Services.AddIntegrationEventHandler<PaymentRequestedIntegrationEvent, PaymentRequestedHandler>();
builder.Services.AddHostedService<PaymentExpiryService>();
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/");
    options.AddAudiences("api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthorization();
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

app.MapGet("/api/payments/orders/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, ISender sender, CancellationToken ct) =>
{
    var subject = user.FindFirstValue("sub") ?? string.Empty;
    var canReadAll = user.IsInRole("admin") || user.IsInRole("operator");
    var payment = await sender.Send(new GetPaymentByOrderQuery(orderId, subject, canReadAll), ct);
    return payment is null ? Results.NotFound() : Results.Ok(payment);
}).RequireAuthorization();

app.MapPost("/api/payments/callbacks/alipay", async (
    HttpRequest request,
    IEnumerable<IPaymentNotificationVerifier> verifiers,
    ISender sender,
    CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var values = form.ToDictionary(x => x.Key, x => x.Value.ToString());
    var verified = await verifiers.Single(x => x.Channel == PaymentChannel.Alipay)
        .VerifyAsync(new PaymentNotification(values, string.Empty), ct);
    if (!verified.Succeeded)
        return Results.BadRequest();

    await sender.Send(new ConfirmPaymentCommand(
        verified.MerchantTradeNo,
        verified.ProviderTransactionId,
        verified.Amount,
        verified.Currency,
        PaymentChannel.Alipay), ct);
    return Results.Text("success");
}).AllowAnonymous();

app.MapPost("/api/payments/callbacks/wechatpay", async (
    HttpRequest request,
    IEnumerable<IPaymentNotificationVerifier> verifiers,
    ISender sender,
    CancellationToken ct) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);
    string Header(string name) => request.Headers[name].ToString();
    var notification = new PaymentNotification(
        new Dictionary<string, string>(), body, Header("Request-ID"), Header("Wechatpay-Serial"),
        Header("Wechatpay-Timestamp"), Header("Wechatpay-Nonce"), Header("Wechatpay-Signature"),
        Header("Wechatpay-Signature-Type"));
    var verified = await verifiers.Single(x => x.Channel == PaymentChannel.WeChatPay)
        .VerifyAsync(notification, ct);
    if (!verified.Succeeded)
        return Results.BadRequest();

    await sender.Send(new ConfirmPaymentCommand(
        verified.MerchantTradeNo,
        verified.ProviderTransactionId,
        verified.Amount,
        verified.Currency,
        PaymentChannel.WeChatPay), ct);
    return Results.Json(new { code = "SUCCESS", message = "成功" });
}).AllowAnonymous();

app.Run();
