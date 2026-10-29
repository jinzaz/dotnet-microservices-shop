using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MSA.OrderService.Application.CreateOrder;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace MSA.OrderService.Middleware;

/// <summary>
/// 下单两道闸：按用户的令牌桶挡刷单，全局并发上限挡住连接池前的排队。
/// 一个端点只能挂一个策略，所以按用户那道挂在 GlobalLimiter 上（只对挂了 <see cref="PolicyName"/> 的端点生效）。
/// 中间件先过 GlobalLimiter 再过端点策略：超频的用户当场 429，不去占全局的排队位。
/// </summary>
internal static class OrderSubmissionRateLimiting
{
    public const string PolicyName = "OrderSubmission";

    public static IServiceCollection AddOrderSubmissionRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName != PolicyName
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetTokenBucketLimiter(
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
            // 所有用户共用一个分区：同时处理的下单最多 N 个，其余按到达顺序排队，队满直接 429。
            options.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetConcurrencyLimiter(PolicyName, _ =>
                {
                    var checkout = httpContext.RequestServices.GetRequiredService<IOptions<OrderCheckoutOptions>>().Value;
                    return new ConcurrencyLimiterOptions
                    {
                        PermitLimit = checkout.MaxConcurrentSubmissions!.Value,
                        QueueLimit = checkout.SubmissionQueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    };
                }));
        });
}
