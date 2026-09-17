using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.Contracts;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using MSA.InventoryService.Integration;

namespace MSA.CommerceTests;

[TestClass]
public sealed class InventoryApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RequestHasher_EquivalentRequestsIgnoreLineOrderSkuCaseAndWhitespace()
    {
        var orderId = Guid.NewGuid();
        var first = Submitted(orderId, [new("sku-b", 1, 5m), new("SKU-A", 2, 10m)]);
        var second = Submitted(orderId, [new(" sku-a ", 2, 10m), new("SKU-B", 1, 5m)]);
        var mixedCase = Submitted(orderId, [new("SKU-B", 1, 5m), new("sku-a", 2, 10m)]);

        var hash = InventoryRequestHasher.Compute(first);

        Assert.AreEqual(64, hash.Length);
        Assert.AreEqual(hash, InventoryRequestHasher.Compute(second));
        Assert.AreEqual(hash, InventoryRequestHasher.Compute(mixedCase));
    }

    [TestMethod]
    public void RequestHasher_AnyReservationRelevantChange_ProducesDifferentHash()
    {
        var orderId = Guid.NewGuid();
        var baseline = InventoryRequestHasher.Compute(Submitted(orderId, [new("SKU-A", 2, 10m)]));

        Assert.AreNotEqual(baseline, InventoryRequestHasher.Compute(Submitted(orderId, [new("SKU-A", 3, 10m)])));
        Assert.AreNotEqual(baseline, InventoryRequestHasher.Compute(Submitted(orderId, [new("SKU-A", 2, 10m)], amount: 21m)));
        Assert.AreNotEqual(baseline, InventoryRequestHasher.Compute(
            Submitted(orderId, [new("SKU-A", 2, 10m, OfferId: Guid.NewGuid())])));
    }

    [TestMethod]
    public void RequestHasher_OfferAwareRequests_IgnoreLineOrderAndSkuCase()
    {
        var orderId = Guid.NewGuid();
        var offer = Guid.NewGuid();

        Assert.AreEqual(
            InventoryRequestHasher.Compute(Submitted(orderId, [new("sku-b", 1, 5m, OfferId: offer), new("SKU-A", 1, 5m)])),
            InventoryRequestHasher.Compute(Submitted(orderId, [new("SKU-A", 1, 5m), new("SKU-B", 1, 5m, OfferId: offer)])));
    }

    [TestMethod]
    public async Task CacheInvalidator_EvictsQueuedKeysOnlyWhenFlushed()
    {
        var cache = NewCache();
        var allocationId = Guid.NewGuid();
        await cache.SetStringAsync(InventoryCacheKeys.Stock("SKU-1"), "stale", TestContext.CancellationToken);
        await cache.SetStringAsync(InventoryCacheKeys.Allocation(allocationId), "stale", TestContext.CancellationToken);
        var invalidator = new InventoryCacheInvalidator(cache, NullLogger<InventoryCacheInvalidator>.Instance);

        invalidator.StockChanged(" sku-1 ");
        invalidator.AllocationChanged(allocationId);
        Assert.AreEqual("stale", await cache.GetStringAsync(InventoryCacheKeys.Stock("SKU-1"), TestContext.CancellationToken), "Eviction must wait for commit.");

        await invalidator.FlushAsync();

        Assert.IsNull(await cache.GetStringAsync(InventoryCacheKeys.Stock("SKU-1"), TestContext.CancellationToken));
        Assert.IsNull(await cache.GetStringAsync(InventoryCacheKeys.Allocation(allocationId), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task CacheInvalidationBehavior_FlushesAfterSuccessAndDiscardsAfterFailure()
    {
        var cache = NewCache();
        await cache.SetStringAsync(InventoryCacheKeys.Stock("SKU-OK"), "stale", TestContext.CancellationToken);
        await cache.SetStringAsync(InventoryCacheKeys.Stock("SKU-FAIL"), "current", TestContext.CancellationToken);
        var invalidator = new InventoryCacheInvalidator(cache, NullLogger<InventoryCacheInvalidator>.Instance);
        var behavior = new InventoryCacheInvalidationBehavior<object, int>(invalidator);

        var response = await behavior.Handle(new object(), _ =>
        {
            invalidator.StockChanged("SKU-OK");
            return Task.FromResult(42);
        }, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => behavior.Handle(new object(), _ =>
        {
            invalidator.StockChanged("SKU-FAIL");
            throw new InvalidOperationException("rolled back");
        }, CancellationToken.None));
        await invalidator.FlushAsync();

        Assert.AreEqual(42, response);
        Assert.IsNull(await cache.GetStringAsync(InventoryCacheKeys.Stock("SKU-OK"), TestContext.CancellationToken));
        Assert.AreEqual("current", await cache.GetStringAsync(InventoryCacheKeys.Stock("SKU-FAIL"), TestContext.CancellationToken),
            "A rolled-back command must not evict entries it never changed.");
    }

    [TestMethod]
    public void NormalizeSku_IsSharedByStockLinesAndAllocations()
    {
        Assert.AreEqual("SKU-1", StockItem.NormalizeSku(" sku-1 "));
        Assert.AreEqual("SKU-1", new InventoryReservationLine(" sku-1 ", 1).Sku);
        Assert.AreEqual("SKU-1", new InventoryAllocation(Guid.NewGuid(), Guid.NewGuid(), "sku-1", 1,
            Now, Now.AddHours(1), 1, Now).Sku);
        Assert.ThrowsExactly<ArgumentException>(() => StockItem.NormalizeSku(" "));
        Assert.ThrowsExactly<ArgumentNullException>(() => new InventoryReservationLine(null!, 1));
    }

    [TestMethod]
    public void ReservationLine_AllocationAndOfferMustBePaired()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new InventoryReservationLine("SKU", 1, Guid.NewGuid(), null));
        Assert.ThrowsExactly<ArgumentException>(() => new InventoryReservationLine("SKU", 1, Guid.Empty, Guid.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new InventoryReservationLine("SKU", 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new InventoryReservation(Guid.NewGuid(), Guid.NewGuid(), [new InventoryReservationLine("SKU", 1)], Now, Now));
    }

    [TestMethod]
    public void Reservation_CommitIsIdempotentAndBlocksRelease()
    {
        var reservation = new InventoryReservation(Guid.NewGuid(), Guid.NewGuid(), " buyer ",
            [new InventoryReservationLine("SKU", 1, Guid.NewGuid(), Guid.NewGuid())], Now, Now.AddMinutes(10));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => reservation.TryCommit(Now.AddTicks(-1)));
        Assert.IsTrue(reservation.TryCommit(Now.AddMinutes(1)));
        Assert.IsFalse(reservation.TryCommit(Now.AddMinutes(2)));
        Assert.IsFalse(reservation.TryRelease(Now.AddMinutes(3), "late"));

        Assert.AreEqual("buyer", reservation.UserId);
        Assert.IsTrue(reservation.HasAllocations);
        Assert.AreEqual(ReservationStatus.Committed, reservation.Status);
        Assert.AreEqual(1L, reservation.Version);
    }

    [TestMethod]
    public void RequestDecision_ReplayRequiresIdenticalPayloadAndMatchingOutcomeData()
    {
        var hash = new string('A', 64);
        var accepted = InventoryRequestDecision.Accept(Guid.NewGuid(), hash, Guid.NewGuid(), Now);

        accepted.EnsureMatches(hash);
        Assert.IsTrue(accepted.IsAccepted);
        Assert.ThrowsExactly<InvalidOperationException>(() => accepted.EnsureMatches(new string('B', 64)));
        Assert.ThrowsExactly<ArgumentException>(() => InventoryRequestDecision.Reject(Guid.NewGuid(), hash, " ", Now));
        Assert.ThrowsExactly<ArgumentException>(() => InventoryRequestDecision.Accept(Guid.NewGuid(), "short", Guid.NewGuid(), Now));
    }

    [TestMethod]
    public void StockMovement_RecordsBalancesAfterChangeAndValidatesIdempotencyMetadata()
    {
        var stock = new StockItem(Guid.NewGuid(), "SKU", 5);
        stock.Adjust(3);

        var movement = StockMovement.Record(stock, StockMovementType.StockAdjusted, 3, 0,
            StockMovementReferenceType.None, null, " cycle count ", Now, " operator ", " key-1 ", new string('C', 64));

        Assert.AreEqual(8, movement.OnHandAfter);
        Assert.AreEqual("cycle count", movement.Reason);
        Assert.AreEqual("operator", movement.OperatorId);
        Assert.AreEqual("key-1", movement.IdempotencyKey);
        Assert.ThrowsExactly<ArgumentException>(() => StockMovement.Record(stock, StockMovementType.StockAdjusted, 1, 0,
            StockMovementReferenceType.None, null, "reason", Now, "operator", "key-1"));
        Assert.ThrowsExactly<ArgumentException>(() => StockMovement.Record(stock, StockMovementType.InventoryReserved, 0, 1,
            StockMovementReferenceType.Reservation, null, "reason", Now));
        Assert.ThrowsExactly<ArgumentException>(() => StockMovement.Record(stock, StockMovementType.StockAdjusted, 0, 0,
            StockMovementReferenceType.None, null, "reason", Now));
    }

    [TestMethod]
    public void AdjustStockValidator_RejectsZeroDeltaAndMissingIdempotencyKey()
    {
        var result = new AdjustStockCommandValidator().Validate(new AdjustStockCommand("SKU", 0, "reason", "", "operator"));

        CollectionAssert.AreEquivalent(
            new[] { nameof(AdjustStockCommand.QuantityDelta), nameof(AdjustStockCommand.IdempotencyKey) },
            result.Errors.Select(error => error.PropertyName).ToArray());
    }

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

    private static OrderSubmittedIntegrationEvent Submitted(
        Guid orderId,
        IReadOnlyList<OrderItemContract> items,
        decimal amount = 20m) => new()
    {
        OrderId = orderId,
        UserId = "buyer-1",
        Items = items,
        Amount = amount,
        Currency = "CNY",
        PaymentMethod = "Alipay",
        PaymentDueOnUtc = Now.AddMinutes(15)
    };
}
