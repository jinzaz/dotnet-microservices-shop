using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.EventBusOutbox;
using MSA.OrderService.Application;
using MSA.OrderService.Application.CancelOrder;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.ManageFulfillment;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.OrderService.Integration;
using MSA.Saga;

namespace MSA.CommerceTests;

[TestClass]
public sealed class OrderApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly string PriceHash = new('A', 64);

    [TestMethod]
    public void RequestFingerprint_IsStableForEquivalentPayloadAndSensitiveToBusinessChanges()
    {
        var address = Address();
        var fingerprint = OrderRequestFingerprint.Create(
            " buyer-1 ", "alipay", [("sku-b", 1, null), ("SKU-A", 2, null)], ["save10"], address, null, null);

        Assert.AreEqual(fingerprint, OrderRequestFingerprint.Create(
            "buyer-1", "ALIPAY", [(" sku-a ", 2, null), ("SKU-B", 1, null)], [" SAVE10 "], address, null, null));
        Assert.AreNotEqual(fingerprint, OrderRequestFingerprint.Create(
            "buyer-1", "ALIPAY", [("SKU-A", 3, null), ("SKU-B", 1, null)], ["SAVE10"], address, null, null));
        Assert.AreNotEqual(fingerprint, OrderRequestFingerprint.Create(
            "buyer-1", "ALIPAY", [("SKU-A", 2, null), ("SKU-B", 1, null)], ["SAVE10"], address, Guid.NewGuid(), 3));
        Assert.ThrowsExactly<ArgumentException>(() => OrderRequestFingerprint.Create(
            "buyer-1", "ALIPAY", [("SKU-A", 2, null)], [], address, Guid.NewGuid(), null));
    }

    [TestMethod]
    public void CouponDigest_NormalizesCodeBeforeHashing()
    {
        Assert.AreEqual(CouponCode.Digest("SAVE10"), CouponCode.Digest(" save10 "));
        Assert.AreEqual(64, CouponCode.Digest("SAVE10").Length);
        Assert.ThrowsExactly<ArgumentException>(() => CouponCode.Digest(" "));
    }

    [TestMethod]
    public void PercentageOff_RespectsEligibleSubtotalRemainingAmountAndCap()
    {
        var context = new PromotionEvaluationContext([Item(100m, 1)], Subtotal: 150m, EligibleSubtotal: 100m);
        var benefit = new PercentageOffBenefit();

        Assert.AreEqual(10m, benefit.Calculate("{\"percentage\":10}", context, 150m));
        Assert.AreEqual(5m, benefit.Calculate("{\"percentage\":10}", context, 50m));
        Assert.AreEqual(3m, benefit.Calculate("{\"percentage\":10,\"maximumDiscount\":3}", context, 150m));
        Assert.ThrowsExactly<InvalidOperationException>(() => benefit.Calculate("{\"percentage\":101}", context, 150m));
    }

    [TestMethod]
    public void FixedAmountOffAndRules_UseConfiguredThresholds()
    {
        var context = new PromotionEvaluationContext([Item(20m, 2), Item(5m, 1)], Subtotal: 45m, EligibleSubtotal: 40m);

        Assert.AreEqual(40m, new FixedAmountOffBenefit().Calculate("{\"amount\":60}", context, 45m));
        Assert.AreEqual(30m, new FixedAmountOffBenefit().Calculate("{\"amount\":60}", context, 30m));
        Assert.IsTrue(new MinimumOrderAmountRule().IsSatisfied("{\"amount\":40}", context));
        Assert.IsFalse(new MinimumOrderAmountRule().IsSatisfied("{\"amount\":40.01}", context));
        Assert.IsTrue(new MinimumItemQuantityRule().IsSatisfied("{\"quantity\":3}", context));
        Assert.IsFalse(new MinimumItemQuantityRule().IsSatisfied("{\"quantity\":4}", context));
        Assert.ThrowsExactly<InvalidOperationException>(() => new MinimumItemQuantityRule().IsSatisfied("{\"quantity\":0}", context));
    }

    [TestMethod]
    public void CreateOrderValidator_ReportsInvalidLinesInsteadOfThrowing()
    {
        var validator = new CreateOrderCommandValidator();

        var nullSku = validator.Validate(Command([new CreateOrderItem(null!, 1)]));
        var duplicate = validator.Validate(Command([new CreateOrderItem("sku-1", 1), new CreateOrderItem(" SKU-1 ", 2)]));
        var partialBasket = validator.Validate(Command([new CreateOrderItem("SKU-1", 1)]) with { SourceBasketId = Guid.NewGuid() });
        var unsupportedPayment = validator.Validate(Command([new CreateOrderItem("SKU-1", 1)]) with { PaymentMethod = "Cash" });
        var valid = validator.Validate(Command([new CreateOrderItem("SKU-1", 1)]));

        Assert.IsFalse(nullSku.IsValid);
        Assert.IsTrue(duplicate.Errors.Any(error => error.ErrorMessage.Contains("Duplicate SKU", StringComparison.Ordinal)));
        Assert.IsFalse(partialBasket.IsValid);
        Assert.IsFalse(unsupportedPayment.IsValid);
        Assert.IsTrue(valid.IsValid, string.Join("; ", valid.Errors));
    }

    [TestMethod]
    public async Task UpdateFulfillment_LocksBeforeLoadingAndShipsWithInjectedClock()
    {
        var order = ConfirmedOrder();
        var calls = new List<string>();
        var fulfillmentLock = new Mock<IOrderFulfillmentLock>();
        fulfillmentLock.Setup(value => value.AcquireAsync(order.Id, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("lock")).Returns(Task.CompletedTask);
        var orders = new Mock<IOrderRepository>();
        orders.Setup(value => value.GetWithFulfillmentHistoryAsync(order.Id, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("load")).ReturnsAsync(order);
        var handler = new UpdateOrderFulfillmentCommandHandler(orders.Object, fulfillmentLock.Object, new TestClock(Now.AddHours(1)));
        var command = new UpdateOrderFulfillmentCommand(order.Id, "ship-1", OrderFulfillmentStatus.Shipped,
            "sf", "Shunfeng", "track-1", null, "operator");

        var first = await handler.Handle(command, CancellationToken.None);
        var replay = await handler.Handle(command with { CarrierCode = " SF " }, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "lock", "load", "lock", "load" }, calls);
        Assert.IsTrue(first.Changed);
        Assert.IsFalse(replay.Changed, "A normalized replay of the same request is idempotent.");
        Assert.AreEqual(OrderFulfillmentStatus.Shipped, first.Status);
        Assert.AreEqual(Now.AddHours(1), first.UpdatedOnUtc);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.Handle(
            command with { TrackingNumber = "TRACK-2" }, CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateFulfillment_UnknownOrder_ThrowsNotFound()
    {
        var handler = new UpdateOrderFulfillmentCommandHandler(
            Mock.Of<IOrderRepository>(), Mock.Of<IOrderFulfillmentLock>(), new TestClock(Now));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => handler.Handle(
            new UpdateOrderFulfillmentCommand(Guid.NewGuid(), "key", OrderFulfillmentStatus.Delivered, null, null, null, null, "operator"),
            CancellationToken.None));
    }

    [TestMethod]
    public void FulfillmentValidator_RequiresCarrierDataOnlyForShipment()
    {
        var validator = new UpdateOrderFulfillmentCommandValidator();

        Assert.IsFalse(validator.Validate(new UpdateOrderFulfillmentCommand(
            Guid.NewGuid(), "key", OrderFulfillmentStatus.Shipped, null, null, null, null, "operator")).IsValid);
        Assert.IsTrue(validator.Validate(new UpdateOrderFulfillmentCommand(
            Guid.NewGuid(), "key", OrderFulfillmentStatus.InTransit, null, null, null, null, "operator")).IsValid);
        Assert.IsFalse(validator.Validate(new UpdateOrderFulfillmentCommand(
            Guid.NewGuid(), "key", OrderFulfillmentStatus.Cancelled, null, null, null, null, "operator")).IsValid);
    }

    [TestMethod]
    public void Ship_InvalidInput_LeavesNoPartialCarrierState()
    {
        var order = ConfirmedOrder();
        var version = order.Version;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => order.Ship("ship-1", new string('B', 64), "SF",
            new string('x', 121), "TRACK-1", null, "operator", Now.AddHours(1)));
        Assert.ThrowsExactly<ArgumentException>(() => order.Ship("ship-1", new string('B', 64), "SF",
            "Shunfeng", "TRACK-1", null, " ", Now.AddHours(1)));
        Assert.ThrowsExactly<ArgumentException>(() => order.MarkInTransit("transit", "not-a-hash", null, "operator", Now));

        Assert.IsNull(order.CarrierCode);
        Assert.IsNull(order.TrackingNumber);
        Assert.IsNull(order.ShippedOnUtc);
        Assert.AreEqual(version, order.Version);
        Assert.AreEqual(OrderFulfillmentStatus.AwaitingShipment, order.FulfillmentStatus);
    }

    [TestMethod]
    public void SagaData_RoundTripsAndReportsCompensationCompletion()
    {
        var data = OrderFulfillmentSagaData.Start(ConfirmedOrder()) with
        {
            RequiresInventoryCompensation = true,
            RequiresPaymentCompensation = true,
            InventoryCompensated = true
        };

        var parsed = OrderFulfillmentSagaData.Parse(JsonSerializer.Serialize(data));

        Assert.AreEqual(data, parsed);
        Assert.IsFalse(parsed.IsCompensationComplete);
        Assert.IsTrue((parsed with { PaymentCompensated = true }).IsCompensationComplete);
        Assert.IsTrue((parsed with { RequiresInventoryCompensation = false, RequiresPaymentCompensation = false, InventoryCompensated = false }).IsCompensationComplete);
        Assert.ThrowsExactly<InvalidOperationException>(() => OrderFulfillmentSagaData.Parse("null"));
    }

    [TestMethod]
    public void ProductProjection_NormalizesSkuAndIgnoresStaleOrDuplicateRevisions()
    {
        var projection = new OrderProductProjection(Guid.NewGuid(), Guid.NewGuid(), " sku-1 ", "Product", "Variant",
            "", 10.005m, "cny", true, null, 2, Now);

        Assert.IsFalse(projection.Apply("Old", "Old", "{}", 1m, "CNY", false, null, 1, Now.AddMinutes(1)));
        Assert.IsFalse(projection.Apply("Same", "Same", "{}", 1m, "CNY", false, null, 2, Now));
        Assert.IsTrue(projection.Apply("New", "Variant", "{}", 12m, "usd", false, null, 3, Now.AddMinutes(1)));

        Assert.AreEqual("SKU-1", projection.Sku);
        Assert.AreEqual("New", projection.ProductName);
        Assert.AreEqual("USD", projection.Currency);
        Assert.IsFalse(projection.IsActive);
        Assert.AreEqual(3L, projection.SourceRevision);
    }

    [TestMethod]
    public void AllocationProjection_OutOfOrderOpenCannotReopenNewerClose()
    {
        var projection = new InventoryAllocationProjection(Guid.NewGuid(), Guid.NewGuid(), "sku-1",
            Now, Now.AddHours(1), 2, Now);

        Assert.IsTrue(projection.AcceptsOrders(Now.AddMinutes(1)));
        projection.Close(Now.AddMinutes(10));
        projection.ApplyOpen("SKU-1", Now, Now.AddHours(2), 5, Now.AddMinutes(5));
        projection.Close(Now.AddMinutes(9));

        Assert.IsFalse(projection.AcceptsOrders(Now.AddMinutes(11)));
        Assert.AreEqual(2, projection.PerUserLimit);
        Assert.AreEqual(Now.AddMinutes(10), projection.ClosedOnUtc);
        Assert.AreEqual("SKU-1", projection.Sku);
    }

    [TestMethod]
    public async Task CancelOrder_AwaitingPayment_StartsCompensationAndRejectsOtherUsersAndPaidOrders()
    {
        var order = NewOrder();
        order.InventoryReserved(Guid.NewGuid(), Now.AddMinutes(1));
        var saga = SagaState.Start(order.Id, OrderFulfillmentSagaData.SagaType, OrderFulfillmentSagaData.AwaitingPayment,
            JsonSerializer.Serialize(OrderFulfillmentSagaData.Start(order)), Now);
        var paid = ConfirmedOrder();
        var orders = new Mock<IOrderRepository>();
        orders.Setup(value => value.GetWithFulfillmentHistoryAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        orders.Setup(value => value.GetWithFulfillmentHistoryAsync(paid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(paid);
        var sagas = new Mock<ISagaStore>();
        sagas.Setup(value => value.FindAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(saga);
        var outbox = new Mock<IOutboxStore>();
        var coupons = new Mock<ICouponRedemptionStore>();
        var handler = new CancelOrderCommandHandler(orders.Object, sagas.Object,
            new OrderCancellationWorkflow(outbox.Object, coupons.Object), new TestClock(Now.AddMinutes(5)));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            handler.Handle(new CancelOrderCommand(order.Id, "someone-else", null), CancellationToken.None));
        var result = await handler.Handle(new CancelOrderCommand(order.Id, "buyer-1", " changed my mind "), CancellationToken.None);
        var replay = await handler.Handle(new CancelOrderCommand(order.Id, "buyer-1", null), CancellationToken.None);

        Assert.IsTrue(result.Changed);
        Assert.IsFalse(replay.Changed);
        Assert.AreEqual("Cancelled", result.Status);
        Assert.AreEqual("changed my mind", order.CancellationReason);
        Assert.AreEqual(SagaStatus.Compensating, saga.Status);
        coupons.Verify(value => value.ReleaseAsync(order.Id, Now.AddMinutes(5), It.IsAny<CancellationToken>()), Times.Once);
        // Release inventory, cancel the pending payment, announce the cancellation.
        outbox.Verify(value => value.Add(It.IsAny<OutboxMessage>()), Times.Exactly(3));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            handler.Handle(new CancelOrderCommand(paid.Id, "buyer-1", null), CancellationToken.None));
    }

    [TestMethod]
    public async Task SagaTimeout_RedeliveryBacksOffPerAttemptAndResetsOnNewState()
    {
        var order = ConfirmedOrder();
        await using var db = new OrderDbContext(
            new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var saga = SagaState.Start(order.Id, OrderFulfillmentSagaData.SagaType, OrderFulfillmentSagaData.AwaitingInventoryCommit,
            JsonSerializer.Serialize(OrderFulfillmentSagaData.Start(order)), Now);
        var outbox = new Mock<IOutboxStore>();
        var clock = new TestClock(Now);
        var handler = new OrderSagaTimeoutHandler(db, outbox.Object,
            new OrderCancellationWorkflow(outbox.Object, Mock.Of<ICouponRedemptionStore>()),
            Options.Create(new OrderSagaOptions()), clock, NullLogger<OrderSagaTimeoutHandler>.Instance);

        async Task<double> NextDelayMinutesAsync()
        {
            await handler.HandleTimeoutAsync(saga, CancellationToken.None);
            var delay = (saga.TimeoutOnUtc!.Value - clock.UtcNow).TotalMinutes;
            clock.UtcNow = saga.TimeoutOnUtc.Value;
            return delay;
        }

        CollectionAssert.AreEqual(
            new[] { 10d, 20d, 40d, 60d, 60d },
            new[] { await NextDelayMinutesAsync(), await NextDelayMinutesAsync(), await NextDelayMinutesAsync(),
                await NextDelayMinutesAsync(), await NextDelayMinutesAsync() });
        outbox.Verify(value => value.Add(It.IsAny<OutboxMessage>()), Times.Exactly(5));

        // 进入新状态后计数重新开始，不沿用上一个状态的退避。
        saga.BeginCompensation(OrderFulfillmentSagaData.CompensatingRefund, saga.Data, clock.UtcNow);
        Assert.AreEqual(10d, await NextDelayMinutesAsync());
    }

    [TestMethod]
    public async Task FulfillmentStatusChange_PublishesOnlyStatusesWithSubscribers()
    {
        var outbox = new Mock<IOutboxStore>();
        var published = new List<string>();
        outbox.Setup(value => value.Add(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(message => published.Add(
                JsonSerializer.Deserialize<JsonElement>(message.Payload).GetProperty("Status").GetString()!));
        var handler = new OrderFulfillmentDomainEventHandler(outbox.Object);

        foreach (var status in Enum.GetValues<OrderFulfillmentStatus>())
        {
            await handler.Handle(new OrderFulfillmentStatusChangedDomainEvent(
                Guid.NewGuid(), "buyer-1", null, status, null, null, null, Now), CancellationToken.None);
        }

        CollectionAssert.AreEquivalent(new[] { "Shipped", "Delivered", "ReturnedToSender" }, published);
    }

    private static PricingItem Item(decimal price, int quantity) =>
        new(new OrderProductProjection(Guid.NewGuid(), Guid.NewGuid(), $"SKU-{Guid.NewGuid():N}", "Product", "Variant",
            "{}", price, "CNY", true, null, 1, Now), quantity);

    private static CreateOrderCommand Command(IReadOnlyList<CreateOrderItem> items) =>
        new("buyer-1", "order-key", items, "Alipay", new ShippingAddressInput(
            null, "Buyer", "13800000000", "CN", "Province", "City", "District", "Street", null, null));

    private static ShippingAddressSnapshot Address() =>
        new(null, "Buyer", "13800000000", "CN", "Province", "City", "District", "Street", null, null);

    private static Order NewOrder() =>
        new(
            Guid.NewGuid(), "buyer-1", "order-key", new string('F', 64), "Alipay", Address(),
            [new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "SKU-1", "Product", "Variant", "{}", 1, 10m, 0m)],
            [], "CNY", null, null, Guid.NewGuid(), PriceHash, Now.AddMinutes(20), Now);

    private static Order ConfirmedOrder()
    {
        var order = NewOrder();
        order.InventoryReserved(Guid.NewGuid(), Now.AddMinutes(1));
        order.Confirm(Guid.NewGuid(), 10m, "CNY", PriceHash, Now.AddMinutes(2));
        return order;
    }
}
