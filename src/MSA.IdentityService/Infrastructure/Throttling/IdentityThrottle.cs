using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using MSA.Redis;
using StackExchange.Redis;

namespace MSA.IdentityService.Infrastructure.Throttling;

public enum ThrottlePolicy
{
    /// <summary>登录、验证码校验、改密码等，按客户端 IP。</summary>
    Authentication,
    /// <summary>注册，按客户端 IP。</summary>
    Registration,
    /// <summary>发短信，按客户端 IP（同一手机号的冷却与小时上限在 SmsVerificationService 里）。</summary>
    SmsDispatch,
    /// <summary>密码登录失败，按"客户端 IP + 账号"：同一 IP 对同一账号连错到上限后暂停，别的 IP 不受影响。</summary>
    LoginFailures
}

/// <summary>配置节 Throttling。公司、学校等共用出口 IP 的用户多时调大对应项。</summary>
public sealed class ThrottleOptions
{
    public const string SectionName = "Throttling";

    public ThrottleRule Authentication { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public ThrottleRule Registration { get; init; } = new() { PermitLimit = 10, WindowSeconds = 3600 };
    public ThrottleRule SmsDispatch { get; init; } = new() { PermitLimit = 5, WindowSeconds = 600 };
    public ThrottleRule LoginFailures { get; init; } = new() { PermitLimit = 5, WindowSeconds = 900 };

    internal ThrottleRule For(ThrottlePolicy policy) => policy switch
    {
        ThrottlePolicy.Authentication => Authentication,
        ThrottlePolicy.Registration => Registration,
        ThrottlePolicy.SmsDispatch => SmsDispatch,
        ThrottlePolicy.LoginFailures => LoginFailures,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}

public sealed class ThrottleRule
{
    public int PermitLimit { get; init; }
    public int WindowSeconds { get; init; }
}

/// <summary>
/// 固定窗口计数放在 Redis：重启不清零，多实例共享。Redis 不可用时放行并记警告——
/// 限流只是减速带，不能让它把登录整个拖垮。
/// </summary>
public sealed class IdentityThrottle(
    IConnectionMultiplexer redis,
    RedisKeyspace keyspace,
    IOptions<ThrottleOptions> options,
    ILogger<IdentityThrottle> logger)
{
    // INCR 与设置过期放在一个脚本里，避免进程在两步之间退出留下永不过期的计数键。
    private const string IncrementScript =
        "local count = redis.call('INCR', KEYS[1]) " +
        "if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end " +
        "return count";

    /// <summary>计一次；超过上限时返回还要等多久，否则 null。</summary>
    public async Task<TimeSpan?> HitAsync(ThrottlePolicy policy, string partition)
    {
        var rule = options.Value.For(policy);
        var key = Key(policy, partition);
        try
        {
            var database = redis.GetDatabase();
            var count = (long)await database.ScriptEvaluateAsync(
                IncrementScript,
                [key],
                [rule.WindowSeconds * 1000L]);
            return count <= rule.PermitLimit ? null : await RetryAfterAsync(database, key, rule);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Throttle {Policy} skipped: Redis is unavailable.", policy);
            return null;
        }
    }

    /// <summary>只看不计：已到上限时返回还要等多久，否则 null。</summary>
    public async Task<TimeSpan?> PeekAsync(ThrottlePolicy policy, string partition)
    {
        var rule = options.Value.For(policy);
        var key = Key(policy, partition);
        try
        {
            var database = redis.GetDatabase();
            var count = (long?)await database.StringGetAsync(key) ?? 0;
            return count < rule.PermitLimit ? null : await RetryAfterAsync(database, key, rule);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Throttle {Policy} skipped: Redis is unavailable.", policy);
            return null;
        }
    }

    public async Task ResetAsync(ThrottlePolicy policy, string partition)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(policy, partition));
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Throttle {Policy} reset skipped: Redis is unavailable.", policy);
        }
    }

    /// <summary>
    /// 按客户端地址分区。RemoteIpAddress 已由转发头中间件还原成真实客户端（nginx → 网关 → 这里，每层只信最右一跳）。
    /// IPv6 按 /64 计：运营商通常给一个用户整段 /64，按单个地址计数等于不限流。
    /// </summary>
    public static string ClientPartition(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            address = new IPAddress(bytes);
        }
        return address.ToString();
    }

    public static IActionResult TooManyRequests(HttpContext context, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "请求过于频繁",
            Detail = seconds >= 60
                ? $"尝试次数过多，请 {(seconds + 59) / 60} 分钟后再试。"
                : $"尝试次数过多，请 {seconds} 秒后再试。"
        })
        { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private RedisKey Key(ThrottlePolicy policy, string partition) =>
        keyspace.Key($"throttle:{policy.ToString().ToLowerInvariant()}:{partition}");

    private static async Task<TimeSpan> RetryAfterAsync(IDatabase database, RedisKey key, ThrottleRule rule) =>
        await database.KeyTimeToLiveAsync(key) ?? TimeSpan.FromSeconds(rule.WindowSeconds);
}

/// <summary>按客户端 IP 限流，超出返回 429 和 Retry-After。替代 ASP.NET 自带的内存限流器（重启清零、多实例各算各的）。</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ThrottleAttribute(ThrottlePolicy policy) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var throttle = context.HttpContext.RequestServices.GetRequiredService<IdentityThrottle>();
        if (await throttle.HitAsync(policy, IdentityThrottle.ClientPartition(context.HttpContext)) is { } retryAfter)
        {
            context.Result = IdentityThrottle.TooManyRequests(context.HttpContext, retryAfter);
            return;
        }

        await next();
    }
}
