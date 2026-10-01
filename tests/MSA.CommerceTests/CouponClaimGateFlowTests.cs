using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.CatalogService.Application;
using MSA.CatalogService.Domain;
using MSA.Redis;

namespace MSA.CommerceTests;

/// <summary>
/// 抢券削峰的业务流程：最后一张被抢走 → 之后的洪峰在 Redis 就被拒掉、不再排队抢数据库行锁；
/// 同一个人连点只放一个进去；后台加库存（活动行落库）后标记被提交后失效清掉，又能抢了。
/// </summary>
[TestClass]
public sealed class CouponClaimGateFlowTests
{
    private static readonly Guid CampaignId = Guid.NewGuid();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AfterTheLastCouponIsClaimed_LaterClaimsNeverReachTheDatabase()
    {
        var markers = new FakeMarkers();
        var gate = Gate(markers, new FakeLocks());
        var databaseCalls = 0;

        var last = await gate.Handle(new ClaimCouponCommand(CampaignId, "u1"), _ =>
        {
            databaseCalls++;
            return Task.FromResult(Result(remaining: 0));
        }, TestContext.CancellationToken);

        for (var shopper = 2; shopper <= 100; shopper++)
        {
            var rejection = await Assert.ThrowsExactlyAsync<CouponClaimRejectedException>(() => gate.Handle(
                new ClaimCouponCommand(CampaignId, $"u{shopper}"),
                _ => { databaseCalls++; return Task.FromResult(Result(remaining: 0)); },
                TestContext.CancellationToken));
            Assert.AreEqual(CouponClaimRejection.SoldOut, rejection.Reason);
        }

        Assert.AreEqual(0, last.RemainingQuantity);
        Assert.AreEqual(1, databaseCalls, "Only the claim that took the last coupon touched the row lock.");

        // 后台加了库存：活动行落库，提交后失效规则（CatalogCacheKeys.Configure）删掉抢光标记，这里直接模拟那次失效。
        await markers.ClearAsync(CatalogCacheKeys.CampaignSoldOut(CampaignId), TestContext.CancellationToken);

        await gate.Handle(new ClaimCouponCommand(CampaignId, "u2"), _ =>
        {
            databaseCalls++;
            return Task.FromResult(Result(remaining: 9));
        }, TestContext.CancellationToken);
        Assert.AreEqual(2, databaseCalls);
    }

    [TestMethod]
    public async Task ADoubleClick_LetsOnlyOneClaimThroughAtATime()
    {
        var gate = Gate(new FakeMarkers(), new FakeLocks());
        var release = new TaskCompletionSource<CouponClaimResultDto>();

        var first = gate.Handle(new ClaimCouponCommand(CampaignId, "u1"), _ => release.Task, TestContext.CancellationToken);
        var second = await Assert.ThrowsExactlyAsync<CouponClaimRejectedException>(() => gate.Handle(
            new ClaimCouponCommand(CampaignId, "u1"),
            _ => Task.FromResult(Result(remaining: 5)),
            TestContext.CancellationToken));
        // 别人不受影响。
        var someoneElse = await gate.Handle(new ClaimCouponCommand(CampaignId, "u2"), _ => Task.FromResult(Result(remaining: 5)), TestContext.CancellationToken);

        release.SetResult(Result(remaining: 4));
        await first;
        var afterwards = await gate.Handle(new ClaimCouponCommand(CampaignId, "u1"), _ => Task.FromResult(Result(remaining: 3)), TestContext.CancellationToken);

        Assert.AreEqual(CouponClaimRejection.InProgress, second.Reason);
        Assert.AreEqual(5, someoneElse.RemainingQuantity);
        Assert.AreEqual(3, afterwards.RemainingQuantity, "Once the first claim finishes the shopper can claim again.");
    }

    [TestMethod]
    public async Task WhenTheDatabaseSaysSoldOut_TheGateRemembersIt()
    {
        var markers = new FakeMarkers();
        var gate = Gate(markers, new FakeLocks());

        await Assert.ThrowsExactlyAsync<CouponClaimRejectedException>(() => gate.Handle(
            new ClaimCouponCommand(CampaignId, "u1"),
            _ => throw new CouponClaimRejectedException(CouponClaimRejection.SoldOut, "sold out"),
            TestContext.CancellationToken));

        Assert.IsTrue(await markers.IsSetAsync(CatalogCacheKeys.CampaignSoldOut(CampaignId), TestContext.CancellationToken));
    }

    private static CouponClaimGate Gate(IRedisMarkers markers, IDistributedLock locks) =>
        new(markers, locks, NullLogger<CouponClaimGate>.Instance);

    private static CouponClaimResultDto Result(int remaining) =>
        new(Guid.NewGuid(), "****ABCD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), remaining, 1, 1);

    private sealed class FakeMarkers : IRedisMarkers
    {
        private readonly HashSet<string> _set = [];

        public Task SetAsync(string name, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_set) _set.Add(name);
            return Task.CompletedTask;
        }

        public Task<bool> IsSetAsync(string name, CancellationToken cancellationToken = default)
        {
            lock (_set) return Task.FromResult(_set.Contains(name));
        }

        public Task ClearAsync(string name, CancellationToken cancellationToken = default)
        {
            lock (_set) _set.Remove(name);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocks : IDistributedLock
    {
        private readonly HashSet<string> _held = [];

        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan lease, CancellationToken cancellationToken = default)
        {
            lock (_held)
            {
                if (!_held.Add(resource))
                    return Task.FromResult<IAsyncDisposable?>(null);
            }
            return Task.FromResult<IAsyncDisposable?>(new Held(() => { lock (_held) _held.Remove(resource); }));
        }

        private sealed class Held(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
