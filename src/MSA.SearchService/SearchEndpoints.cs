using MSA.EventBus.Abstractions;

namespace MSA.SearchService;

internal static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 前台搜索是公开数据，匿名可用；限流在网关（按用户 / IP 的令牌桶）。
        endpoints.MapGet("/search/products", async (
                [AsParameters] ProductSearchRequest request,
                ProductSearchService search,
                CancellationToken cancellationToken) =>
            await search.SearchAsync(request, cancellationToken) is { } result
                ? Results.Ok(result)
                : Unavailable())
            .AllowAnonymous()
            .WithName("SearchProducts")
            .WithSummary("Full-text product search with category facets, price filter and sorting.");

        endpoints.MapGet("/search/suggestions", async (
                string? q,
                ProductSearchService search,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                // 联想词每敲一个字就请求一次，允许浏览器短暂缓存同一个前缀。
                context.Response.Headers.CacheControl = "public, max-age=60";
                return Results.Ok(await search.SuggestAsync(q, cancellationToken));
            })
            .AllowAnonymous()
            .WithName("SuggestProducts");

        endpoints.MapGet("/search/admin/status", (IndexRebuilder rebuilder, CancellationToken cancellationToken) =>
                rebuilder.GetStatusAsync(cancellationToken))
            .RequireAuthorization("search.admin.read");

        endpoints.MapPost("/search/admin/rebuild", async (
                bool? force,
                IndexRebuilder rebuilder,
                IHostApplicationLifetime lifetime) =>
                await rebuilder.TryStartAsync(force == true, lifetime.ApplicationStopping)
                    ? Results.Accepted("/api/search/admin/status")
                    : Results.Problem("A search index rebuild is already running.", statusCode: StatusCodes.Status409Conflict))
            .RequireAuthorization("search.admin.write");

        // 后台「消息运维」页对每个服务调同一组接口。搜索服务没有 Outbox 和 Saga，只有消费失败后进的死信：
        // ES 长时间不可用时同步消息会落在这里，恢复后重放（处理是幂等的），或者直接全量重建后丢弃。
        const string messaging = "/search/admin/messaging";
        endpoints.MapGet($"{messaging}/outbox/failed", () => Results.Ok(Array.Empty<object>()))
            .RequireAuthorization("search.admin.read");
        endpoints.MapGet($"{messaging}/sagas/stalled", () => Results.Ok(Array.Empty<object>()))
            .RequireAuthorization("search.admin.read");
        endpoints.MapGet($"{messaging}/dead-letters", async (IDeadLetterAdmin admin, int? take, CancellationToken cancellationToken) =>
                Results.Ok(await admin.PeekAsync(take ?? 20, cancellationToken)))
            .RequireAuthorization("search.admin.read");
        endpoints.MapPost($"{messaging}/dead-letters/replay", async (IDeadLetterAdmin admin, int? max, CancellationToken cancellationToken) =>
                Results.Ok(new { replayed = await admin.ReplayAsync(max ?? 50, cancellationToken) }))
            .RequireAuthorization("search.admin.write");

        return endpoints;
    }

    /// <summary>503（带 retryAfterSeconds）：前台据此回退到目录服务的数据库搜索，熔断窗口过后再试搜索引擎。</summary>
    private static IResult Unavailable() => Results.Problem(
        "Search is temporarily unavailable.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = (int)SearchCircuitBreaker.OpenFor.TotalSeconds });
}
