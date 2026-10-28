using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Middleware;

namespace MSA.CommerceTests;

/// <summary>
/// 下单的两道闸真实跑一遍 ASP.NET Core 限流中间件：全局并发上限 + 有界队列，以及按用户的令牌桶。
/// </summary>
[TestClass]
public sealed class OrderSubmissionRateLimitingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _release.TrySetResult();
        _client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task Submissions_BeyondTheConcurrencyLimitQueue_AndAreRejectedWhenTheQueueIsFull()
    {
        await StartAsync(maxConcurrent: 1, queueLimit: 1, holdRequests: true);

        // 三个不同的用户：并发上限是全局的，不按用户分。
        var running = Submit("user-a");
        await _started.Task.WaitAsync(Timeout, TestContext.CancellationToken);
        var second = Submit("user-b");
        var third = Submit("user-c");

        // 一个排进队里等着，另一个队满当场 429。
        var rejected = await Task.WhenAny(second, third).WaitAsync(Timeout, TestContext.CancellationToken);
        var queued = rejected == second ? third : second;
        Assert.AreEqual(HttpStatusCode.TooManyRequests, (await rejected).StatusCode);
        Assert.IsFalse(running.IsCompleted);
        Assert.IsFalse(queued.IsCompleted, "排队的请求要等前面的处理完。");

        _release.SetResult();
        Assert.AreEqual(HttpStatusCode.OK, (await running.WaitAsync(Timeout, TestContext.CancellationToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await queued.WaitAsync(Timeout, TestContext.CancellationToken)).StatusCode);
    }

    [TestMethod]
    public async Task OneUserBurstingPastTheTokenBucket_IsRejectedWithoutAffectingOthers()
    {
        await StartAsync(maxConcurrent: 10, queueLimit: 10, holdRequests: false);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
            statuses.Add((await Submit("user-a")).StatusCode);
        var otherUser = await Submit("user-b");

        CollectionAssert.AreEqual(Enumerable.Repeat(HttpStatusCode.OK, 5).ToArray(), statuses.Take(5).ToArray(), "突发额度 5 次。");
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.AreEqual(HttpStatusCode.OK, otherUser.StatusCode);
    }

    private Task<HttpResponseMessage> Submit(string user) =>
        _client.PostAsync($"/submit?user={user}", null, TestContext.CancellationToken);

    private async Task StartAsync(int maxConcurrent, int queueLimit, bool holdRequests)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<OrderCheckoutOptions>(options =>
        {
            options.MaxConcurrentSubmissions = maxConcurrent;
            options.SubmissionQueueLimit = queueLimit;
        });
        builder.Services.AddOrderSubmissionRateLimiting();

        _app = builder.Build();
        // 代替认证：按查询参数给出 sub，令牌桶按它分区。
        _app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", context.Request.Query["user"].ToString())], "test"));
            return next(context);
        });
        _app.UseRateLimiter();
        _app.MapPost("/submit", async () =>
        {
            _started.TrySetResult();
            if (holdRequests)
                await _release.Task;
            return Results.Ok();
        }).RequireRateLimiting(OrderSubmissionRateLimiting.PolicyName);

        await _app.StartAsync(TestContext.CancellationToken);
        _client = _app.GetTestClient();
    }
}
