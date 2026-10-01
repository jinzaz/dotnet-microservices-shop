using System.Threading.Channels;
using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Grpc.Contracts.Inventory;
using MSA.InventoryService.Application;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Grpc;
using MSA.InventoryService.Infrastructure;
using MSA.Redis;

namespace MSA.CommerceTests;

/// <summary>
/// 秒杀余量推流的业务流程：开抢 → 有人下单占掉几件（事务提交）→ 订阅者立刻收到新的剩余量 → 活动关闭后配额从流里消失。
/// 变更通知走的是真实的「提交后失效」拦截器，推流走的是真实的 gRPC 服务实现；
/// 刷新周期故意设得很长，推送只能来自提交后的变更通知，而不是定时轮询。
/// </summary>
[TestClass]
public sealed class FlashSaleStreamFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private const string Sku = "SKU-PHONE-001";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Subscribers_SeeTheRemainingQuantityDropAsSoonAsAReservationCommits()
    {
        var changes = new InMemoryChangeFeed();
        var database = Guid.NewGuid().ToString();
        var clock = new TestClock(Now);
        var allocationId = Guid.NewGuid();
        await using (var seed = Inventory(database, changes))
        {
            seed.InventoryAllocations.Add(new InventoryAllocation(
                allocationId, Guid.NewGuid(), Sku, 10, Now.AddMinutes(-5), Now.AddHours(1), 2, Now.AddMinutes(-10)));
            await seed.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using var reads = Inventory(database, changes);
        var service = new InventoryAvailabilityGrpcService(
            InProcessGrpc.Sender<GetSkuOffersQuery, IReadOnlyList<InventoryAllocationDto>>(
                new GetSkuOffersQueryHandler(new InventoryReadModel(reads, NullCacheStore.Instance, clock))),
            changes,
            Microsoft.Extensions.Options.Options.Create(new OfferStreamOptions
            {
                CoalesceWindow = TimeSpan.FromMilliseconds(20),
                RefreshInterval = TimeSpan.FromMinutes(10)
            }),
            NullLogger<InventoryAvailabilityGrpcService>.Instance);

        using var watching = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var stream = new CollectingStream();
        var watch = service.WatchSkuOffers(
            new WatchSkuOffersRequest { Sku = " sku-phone-001 " },
            stream,
            InProcessGrpc.Context(watching.Token));

        // 订阅一建立就先拿到完整快照。
        var opening = await stream.NextAsync(TestContext.CancellationToken);
        Assert.AreEqual("Active", opening.Offers.Single().State);
        Assert.AreEqual(10, opening.Offers.Single().AvailableQuantity);

        // 有人抢到 3 件：预占事务提交 → 拦截器发出变更通知 → 推流立刻读到新的余量。
        await ChangeAllocationAsync(database, changes, allocationId, allocation => Assert.IsTrue(allocation.TryReserve(3, Now)));
        var afterReservation = await stream.NextAsync(TestContext.CancellationToken);
        Assert.AreEqual(7, afterReservation.Offers.Single().AvailableQuantity);

        // 活动被后台关闭：配额从流里消失，商品页的秒杀角标随之撤下。
        await ChangeAllocationAsync(database, changes, allocationId, allocation => allocation.Close(Now));
        var afterClose = await stream.NextAsync(TestContext.CancellationToken);
        Assert.IsEmpty(afterClose.Offers);

        // 客户端断开后推流正常结束，订阅被释放。
        await watching.CancelAsync();
        await watch;
    }

    [TestMethod]
    public async Task AChangeThatRollsBack_DoesNotWakeSubscribers()
    {
        var changes = new InMemoryChangeFeed();
        var woken = 0;
        await using var subscription = await changes.SubscribeAsync(
            InventoryCacheKeys.OffersChangedTopic(Sku), _ => Interlocked.Increment(ref woken), TestContext.CancellationToken);
        var database = Guid.NewGuid().ToString();
        var allocationId = Guid.NewGuid();
        await using (var seed = Inventory(database, changes))
        {
            seed.InventoryAllocations.Add(new InventoryAllocation(
                allocationId, Guid.NewGuid(), Sku, 10, Now.AddMinutes(-5), Now.AddHours(1), 2, Now.AddMinutes(-10)));
            await seed.SaveChangesAsync(TestContext.CancellationToken);
        }
        Assert.AreEqual(1, woken, "Opening the offer is itself a change subscribers should hear about.");

        // 保存失败（这里用并发冲突模拟）：什么都没提交，就不能通知任何人。
        await using var db = Inventory(database, changes);
        var allocation = await db.InventoryAllocations.SingleAsync(TestContext.CancellationToken);
        allocation.TryReserve(1, Now);
        db.Entry(allocation).Property(nameof(InventoryAllocation.Version)).OriginalValue = -1L;
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(TestContext.CancellationToken));

        Assert.AreEqual(1, woken);
    }

    private static InventoryDbContext Inventory(string database, IChangeFeed changes)
    {
        var rules = new CacheInvalidationRules();
        InventoryCacheKeys.Configure(rules);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(database)
            .AddInterceptors(new CacheInvalidationInterceptor(
                rules, NullCacheStore.Instance, changes, NullLogger<CacheInvalidationInterceptor>.Instance))
            .Options;
        return new InventoryDbContext(options, Mock.Of<IMediator>());
    }

    private async Task ChangeAllocationAsync(
        string database,
        IChangeFeed changes,
        Guid allocationId,
        Action<InventoryAllocation> change)
    {
        await using var db = Inventory(database, changes);
        var allocation = await db.InventoryAllocations.SingleAsync(value => value.Id == allocationId, TestContext.CancellationToken);
        change(allocation);
        await db.SaveChangesAsync(TestContext.CancellationToken);
    }

    private sealed class CollectingStream : IServerStreamWriter<SkuOffersSnapshot>
    {
        private readonly Channel<SkuOffersSnapshot> _messages = Channel.CreateUnbounded<SkuOffersSnapshot>();

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(SkuOffersSnapshot message) => WriteAsync(message, CancellationToken.None);

        // 真实的服务端写入器实现了带取消的重载，服务也调用它；接口的默认实现遇到可取消的令牌会直接抛出。
        public Task WriteAsync(SkuOffersSnapshot message, CancellationToken cancellationToken) =>
            _messages.Writer.WriteAsync(message, cancellationToken).AsTask();

        public async Task<SkuOffersSnapshot> NextAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return await _messages.Reader.ReadAsync(timeout.Token);
        }
    }
}
