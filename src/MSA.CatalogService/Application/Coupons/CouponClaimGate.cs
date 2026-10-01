using MediatR;
using Microsoft.Extensions.Logging;
using MSA.CatalogService.Domain;
using MSA.Redis;

namespace MSA.CatalogService.Application;

/// <summary>
/// 抢券的削峰闸门，挂在事务之外（注册顺序在应用管道之前）：
/// <list type="number">
/// <item>已抢光：Redis 里一次 EXISTS 就拒掉，不再排队去抢活动行锁 —— 抢光之后的洪峰全部挡在数据库之外；</item>
/// <item>同一个人连点 / 脚本并发：同一时刻只放一个请求进去，其余立刻拒绝，不占行锁排队。</item>
/// </list>
/// 闸门只会「多拒」、不会「多发」：真正的数量、每日、每人限额仍在行锁事务里判定。
/// 抢光标记在活动行任何一次落库提交后被清掉（见 <see cref="CatalogCacheKeys.Configure"/>），
/// 后台加库存、恢复活动都会自然解除；Redis 不可用时闸门直接放行。
/// </summary>
internal sealed class CouponClaimGate(
    IRedisMarkers markers,
    IDistributedLock locks,
    ILogger<CouponClaimGate> logger) : IPipelineBehavior<ClaimCouponCommand, CouponClaimResultDto>
{
    private static readonly TimeSpan InFlightLease = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SoldOutMarker = TimeSpan.FromMinutes(10);

    public async Task<CouponClaimResultDto> Handle(
        ClaimCouponCommand request,
        RequestHandlerDelegate<CouponClaimResultDto> next,
        CancellationToken cancellationToken)
    {
        var soldOutKey = CatalogCacheKeys.CampaignSoldOut(request.CampaignId);
        if (await IsSoldOutAsync(soldOutKey, cancellationToken))
            throw new CouponClaimRejectedException(CouponClaimRejection.SoldOut, "The campaign has been fully claimed.");

        await using var inFlight = await TryEnterAsync(request, cancellationToken);
        if (ReferenceEquals(inFlight, Entry.Busy))
        {
            throw new CouponClaimRejectedException(
                CouponClaimRejection.InProgress,
                "A claim for this campaign is already being processed.");
        }

        try
        {
            var result = await next();
            if (result.RemainingQuantity == 0)
                await MarkSoldOutAsync(soldOutKey);
            return result;
        }
        catch (CouponClaimRejectedException rejection) when (rejection.Reason == CouponClaimRejection.SoldOut)
        {
            await MarkSoldOutAsync(soldOutKey);
            throw;
        }
    }

    private async Task<bool> IsSoldOutAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await markers.IsSetAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Sold-out gate unavailable; letting the claim through to the database.");
            return false;
        }
    }

    private async Task<Entry> TryEnterAsync(ClaimCouponCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var lease = await locks.TryAcquireAsync(
                CatalogCacheKeys.ClaimInFlight(request.CampaignId, request.UserId),
                InFlightLease,
                cancellationToken);
            return lease is null ? Entry.Busy : new Entry(lease);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Claim de-duplication unavailable; the row lock still serializes claims.");
            return Entry.Unguarded;
        }
    }

    private async Task MarkSoldOutAsync(string key)
    {
        try
        {
            await markers.SetAsync(key, SoldOutMarker, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record the sold-out marker; later claims will be checked by the database.");
        }
    }

    private sealed class Entry(IAsyncDisposable? lease) : IAsyncDisposable
    {
        public static readonly Entry Busy = new(null);
        public static readonly Entry Unguarded = new(null);

        public ValueTask DisposeAsync() => lease?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
