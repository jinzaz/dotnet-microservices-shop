using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using MSA.EventBus.DependencyInjection;
using MSA.OrderService;
using FluentValidation;
using MSA.EventBusOutbox.Extensions;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Saga.Extensions;
using MSA.Contracts;
using MSA.OrderService.Integration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");
builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<MSADbContext>(sp => sp.GetRequiredService<OrderDbContext>());
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<CreateOrderCommand>());
builder.Services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
builder.Services.AddMsaApplicationPipeline();

// RabbitMQ/EventBus registration via messaging extension (will register SagaCoordinator, repositories and eventbus)
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddRabbitMQEventBus(opts => { opts.HostName = rabbitHost; opts.ExchangeName = "events"; opts.Port = 5672; opts.UserName = builder.Configuration["RabbitMQ:User"] ?? "guest"; opts.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest"; });
builder.Services.AddOutbox<OrderDbContext>();
builder.Services.AddSaga<OrderDbContext>();
builder.Services.AddIntegrationEventHandler<ProductChangedIntegrationEvent, ProductChangedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryReservedIntegrationEvent, InventoryReservedHandler>();
builder.Services.AddIntegrationEventHandler<InventoryRejectedIntegrationEvent, InventoryRejectedHandler>();
builder.Services.AddIntegrationEventHandler<PaymentSucceededIntegrationEvent, PaymentSucceededHandler>();
builder.Services.AddIntegrationEventHandler<PaymentFailedIntegrationEvent, PaymentFailedHandler>();
builder.Services.AddScoped<ISagaTimeoutHandler, OrderSagaTimeoutHandler>();
builder.Services.AddOpenIddict().AddValidation(o => { o.SetIssuer(builder.Configuration["Authentication:Issuer"] ?? "https://localhost:10002/"); o.AddAudiences("api"); o.UseSystemNetHttp(); o.UseAspNetCore(); });
builder.Services.AddAuthorization();

builder.Services.AddOpenTelemetry().WithTracing(otelBuilder =>
{
    var svcName = builder.Environment.ApplicationName ?? "MSA.OrderService";
    otelBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(svcName)).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter();
    if (builder.Configuration.GetValue<bool>("OpenTelemetry:EnableConsoleExporter")) otelBuilder.AddConsoleExporter();
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
