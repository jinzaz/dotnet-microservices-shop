using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MSA.EventBus.Abstractions;

namespace MSA.Repositories.Persistence;

/// <summary>
/// 每个服务共用的最终一致性运维接口：看得见发布不出去的消息、卡住的 Saga 和死信，
/// 并且可以在修复根因后一键重投。
/// </summary>
public static class MessagingAdminEndpoints
{
    /// <summary>
    /// Maps /{serviceRoute}/admin/messaging. Read operations use <paramref name="readPolicy"/>, replays use
    /// <paramref name="writePolicy"/>, so they follow the same authorization model as the rest of the service.
    /// </summary>
    public static IEndpointRouteBuilder MapMessagingAdminEndpoints(
        this IEndpointRouteBuilder endpoints,
        string serviceRoute,
        string readPolicy,
        string writePolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceRoute);
        var read = endpoints.MapGroup($"/{serviceRoute.Trim('/')}/admin/messaging").RequireAuthorization(readPolicy);
        var write = endpoints.MapGroup($"/{serviceRoute.Trim('/')}/admin/messaging").RequireAuthorization(writePolicy);

        read.MapGet("/outbox/failed", async (
            IMessagingOperations operations,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await operations.ListFailedOutboxAsync(take, cancellationToken)));

        write.MapPost("/outbox/failed/requeue", async (
            IMessagingOperations operations,
            TimeProvider clock,
            Guid? messageId = null,
            int max = 100,
            CancellationToken cancellationToken = default) =>
            Results.Ok(new { requeued = await operations.RequeueFailedOutboxAsync(messageId, max, clock.GetUtcNow(), cancellationToken) }));

        read.MapGet("/sagas/stalled", async (
            IMessagingOperations operations,
            TimeProvider clock,
            int stalledMinutes = 30,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await operations.ListStalledSagasAsync(
                clock.GetUtcNow().AddMinutes(-Math.Clamp(stalledMinutes, 1, 10_080)),
                take,
                cancellationToken)));

        read.MapGet("/dead-letters", async (
            HttpContext context,
            int take = 20,
            CancellationToken cancellationToken = default) =>
        {
            var admin = context.RequestServices.GetService<IDeadLetterAdmin>();
            return admin is null
                ? Results.Problem("This service has no dead-letter admin registered.", statusCode: StatusCodes.Status501NotImplemented)
                : Results.Ok(await admin.PeekAsync(take, cancellationToken));
        });

        write.MapPost("/dead-letters/replay", async (
            HttpContext context,
            int max = 50,
            CancellationToken cancellationToken = default) =>
        {
            var admin = context.RequestServices.GetService<IDeadLetterAdmin>();
            return admin is null
                ? Results.Problem("This service has no dead-letter admin registered.", statusCode: StatusCodes.Status501NotImplemented)
                : Results.Ok(new { replayed = await admin.ReplayAsync(max, cancellationToken) });
        });

        return endpoints;
    }
}
