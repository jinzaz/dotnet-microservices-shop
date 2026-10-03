using MSA.Common;
using MSA.Observability;
using MediatR;
using MSA.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using MSA.Contracts;
using MSA.EventBus.DependencyInjection;
using MSA.EventBusRabbitMQ;
using MSA.Infrastrucute;
using MSA.Infrastrucute.Extensions;
using MSA.NotificationService.Application;
using MSA.NotificationService.Domain;
using MSA.NotificationService.Infrastructure;
using MSA.NotificationService.Integration;
using System.Security.Claims;
using System.Text.Json.Serialization;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
// 基础设施地址、OTLP 与 gRPC 下游地址、客户端密钥来自环境变量或仓库根目录的 msa.env，见 MsaEnvironment。
builder.Configuration.AddMsaEnvironment(builder.Environment.ContentRootPath);
var connectionString = builder.Configuration.GetConnectionString("Postgresql");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgresql is required.");

builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString));
// 本服务只消费不发布，但 Inbox 去重记录同样需要保留期清理。
builder.Services.AddMessagingOperations<NotificationDbContext>();
builder.Services.AddScoped<MSADbContext>(services => services.GetRequiredService<NotificationDbContext>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<OrderConfirmedHandler>());
builder.Services.AddMsaApplicationPipeline();
builder.Services.AddRabbitMQEventBus(builder.Configuration.GetSection(RabbitMQOptions.SectionName));
builder.Services.AddIntegrationEventHandler<OrderConfirmedIntegrationEvent, OrderConfirmedHandler>();
builder.Services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, OrderCancelledHandler>();
builder.Services.AddScoped<INotificationSender, LoggingNotificationSender>();
builder.Services.AddHostedService<NotificationDispatchService>();
builder.Services.AddOpenIddict().AddValidation(options =>
{
    options.SetMsaIssuer(builder.Configuration);
    options.AddAudiences("notification-api");
    options.UseSystemNetHttp();
    options.UseAspNetCore();
});
builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
builder.Services.AddMsaApiAuthorization(
    "notification-api",
    "notification.admin.read",
    "notification.admin.write");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
// 链路、指标、日志统一走 OTLP，配置见 MSA.Observability。
builder.AddMsaObservability();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapMessagingAdminEndpoints("notification", "notification.admin.read", "notification.admin.write");
app.MapGet("/notification/users/{userId}", async (
    string userId,
    int skip,
    int take,
    ClaimsPrincipal user,
    NotificationDbContext db,
    CancellationToken ct) =>
{
    if (user.FindFirstValue("sub") != userId)
        return Results.Forbid();

    return await ListUserNotificationsAsync(userId, skip, take, db, ct);
}).RequireAuthorization("notification-api");

app.MapGet("/notification/admin/users/{userId}", (
    string userId,
    int skip,
    int take,
    NotificationDbContext db,
    CancellationToken ct) => ListUserNotificationsAsync(userId, skip, take, db, ct))
    .RequireAuthorization("notification.admin.read");

static async Task<IResult> ListUserNotificationsAsync(
    string userId,
    int skip,
    int take,
    NotificationDbContext db,
    CancellationToken ct)
{
    var rows = await db.Notifications.AsNoTracking()
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedOnUtc)
        .Skip(Math.Max(0, skip))
        .Take(Math.Clamp(take, 1, 100))
        .Select(x => new { x.Id, x.Subject, x.Body, x.Status, x.CreatedOnUtc, x.SentOnUtc })
        .ToListAsync(ct);
    return Results.Ok(rows);
}

app.MapGet("/notification/admin/messages", async (
    ISender sender,
    CancellationToken cancellationToken,
    string? userId = null,
    NotificationStatus? status = null,
    string? templateCode = null,
    int skip = 0,
    int take = 50) => TypedResults.Ok(await sender.Send(
        new ListNotificationsQuery(userId, status, templateCode, skip, take),
        cancellationToken)))
    .RequireAuthorization("notification.admin.read")
    .WithName("ListAdminNotifications")
    .WithSummary("List notification delivery records with admin filters")
    .Produces<NotificationPage>(StatusCodes.Status200OK);

app.MapGet("/notification/admin/templates", async (
    bool? active,
    ISender sender,
    CancellationToken cancellationToken) => TypedResults.Ok(await sender.Send(
        new ListNotificationTemplatesQuery(active),
        cancellationToken)))
    .RequireAuthorization("notification.admin.read")
    .WithName("ListAdminNotificationTemplates")
    .WithSummary("List notification templates")
    .Produces<IReadOnlyList<NotificationTemplateDto>>(StatusCodes.Status200OK);

app.MapPost("/notification/admin/messages", SendAdminNotificationsAsync)
    .RequireAuthorization("notification.admin.write")
    .WithName("SendAdminNotifications")
    .WithSummary("Queue one notification for up to 100 users")
    .Produces<SendAdminNotificationsResult>(StatusCodes.Status202Accepted)
    .ProducesValidationProblem();

app.MapPost("/notification/admin/messages/{notificationId:guid}/retry", RetryNotificationAsync)
    .RequireAuthorization("notification.admin.write")
    .WithName("RetryAdminNotification")
    .WithSummary("Requeue a permanently failed notification")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.Run();

static async Task<IResult> SendAdminNotificationsAsync(
    SendAdminNotificationsRequest request,
    HttpContext context,
    ISender sender,
    CancellationToken cancellationToken)
{
    var errors = new Dictionary<string, string[]>();
    var userIds = request.UserIds?
        .Where(userId => !string.IsNullOrWhiteSpace(userId))
        .Select(userId => userId.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray() ?? [];
    if (userIds.Length is 0 or > 100 || userIds.Any(userId => userId.Length > 200))
        errors[nameof(request.UserIds)] = ["Provide 1-100 user IDs, each no longer than 200 characters."];
    if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 500)
        errors[nameof(request.Subject)] = ["Subject is required and cannot exceed 500 characters."];
    if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 10_000)
        errors[nameof(request.Body)] = ["Body is required and cannot exceed 10000 characters."];
    if (!string.IsNullOrWhiteSpace(request.TemplateCode) && request.TemplateCode.Trim().Length > 100)
        errors[nameof(request.TemplateCode)] = ["Template code cannot exceed 100 characters."];

    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    if (idempotencyKey.Length is 0 or > 100)
        errors["Idempotency-Key"] = ["Idempotency-Key is required and cannot exceed 100 characters."];
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var result = await sender.Send(
        new SendAdminNotificationsCommand(
            userIds,
            string.IsNullOrWhiteSpace(request.TemplateCode) ? "admin-message" : request.TemplateCode.Trim(),
            request.Subject.Trim(),
            request.Body.Trim(),
            idempotencyKey),
        cancellationToken);
    return Results.Accepted("/api/notification/admin/messages", result);
}

static async Task<IResult> RetryNotificationAsync(
    Guid notificationId,
    ISender sender,
    CancellationToken cancellationToken)
{
    var result = await sender.Send(new RetryNotificationCommand(notificationId), cancellationToken);
    if (!result.Found)
        return Results.NotFound();
    return result.Changed
        ? Results.NoContent()
        : Results.Conflict(new ProblemDetails
        {
            Title = "Notification cannot be retried",
            Detail = "Only notifications in the Failed state can be retried."
        });
}

public sealed record SendAdminNotificationsRequest(
    IReadOnlyList<string>? UserIds,
    string Subject,
    string Body,
    string? TemplateCode = null);
