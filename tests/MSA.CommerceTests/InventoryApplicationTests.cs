using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Contracts;
using MSA.EventBusOutbox;
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

    [TestMethod]
    public async Task OrderSubmitted_PastPaymentDeadline_IsRejectedWithoutTouchingStock()
    {
        await using var db = NewInventoryDb();
        var submitted = Submitted(Guid.NewGuid(), [new("SKU-1", 2, 10m)], paymentDueOnUtc: Now);

        await HandleSubmittedAsync(db, submitted);

        var decision = await db.RequestDecisions.SingleAsync(TestContext.CancellationToken);
        Assert.IsFalse(decision.IsAccepted);
        Assert.AreEqual("Order payment deadline has passed.", decision.RejectionReason);
        Assert.AreEqual(0, await db.Reservations.CountAsync(TestContext.CancellationToken));
        Assert.AreEqual(0, (await db.StockItems.SingleAsync(TestContext.CancellationToken)).Reserved);
        var published = await db.OutboxMessages.SingleAsync(TestContext.CancellationToken);
        StringAssert.Contains(published.EventType, nameof(InventoryRejectedIntegrationEvent));
    }

    [TestMethod]
    public async Task OrderSubmitted_ReservationOutlivesThePaymentDeadlineByTheGracePeriod()
    {
        await using var db = NewInventoryDb();
        var submitted = Submitted(Guid.NewGuid(), [new("SKU-1", 2, 10m)]);

        await HandleSubmittedAsync(db, submitted);

        var reservation = await db.Reservations.SingleAsync(TestContext.CancellationToken);
        Assert.AreEqual(submitted.PaymentDueOnUtc!.Value.AddMinutes(30), reservation.ExpiresOnUtc);
        Assert.AreEqual(2, (await db.StockItems.SingleAsync(TestContext.CancellationToken)).Reserved);
    }

    private InventoryDbContext NewInventoryDb()
    {
        var db = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.StockItems.Add(new StockItem(Guid.NewGuid(), "SKU-1", 5));
        db.SaveChanges();
        return db;
    }

    /// <summary>
    /// 直接调用 HandleCoreAsync：外层 HandleAsync 的事务与 Inbox 去重依赖 Postgres（INSERT ... ON CONFLICT），
    /// InMemory 跑不了；预占与拒绝的判断都在 HandleCoreAsync 里。
    /// </summary>
    private async Task HandleSubmittedAsync(InventoryDbContext db, OrderSubmittedIntegrationEvent submitted)
    {
        var handler = new OrderSubmittedHandler(
            db,
            db,
            Mock.Of<IInventoryTransactionLock>(),
            Options.Create(new InventoryReservationOptions()),
            new TestClock(Now));
        var handleCore = typeof(OrderSubmittedHandler).GetMethod("HandleCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)handleCore.Invoke(handler, [submitted, TestContext.CancellationToken])!;
        await db.SaveChangesAsync(TestContext.CancellationToken);
    }

    private static OrderSubmittedIntegrationEvent Submitted(
        Guid orderId,
        IReadOnlyList<OrderItemContract> items,
        decimal amount = 20m,
        DateTimeOffset? paymentDueOnUtc = null) => new()
    {
        OrderId = orderId,
        UserId = "buyer-1",
        Items = items,
        Amount = amount,
        Currency = "CNY",
        PaymentMethod = "Alipay",
        PaymentDueOnUtc = paymentDueOnUtc ?? Now.AddMinutes(15)
    };
}
