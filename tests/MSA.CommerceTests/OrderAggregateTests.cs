using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.OrderService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class OrderAggregateTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly string PriceHash = new('A', 64);

    [TestMethod]
    public void TryCancel_DuplicateDelivery_PreservesFirstReasonAndTerminalState()
    {
        var order = CreateOrder();

        Assert.IsTrue(order.TryCancel("Inventory rejected", Created.AddMinutes(1)));
        Assert.IsFalse(order.TryCancel("Duplicate", Created.AddMinutes(2)));

        Assert.AreEqual(OrderStatus.Cancelled, order.Status);
        Assert.AreEqual(OrderFulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.AreEqual("Inventory rejected", order.CancellationReason);
        Assert.AreEqual(2L, order.Version);
        Assert.AreEqual(2, order.StatusHistory.Count);
    }

    [TestMethod]
    public void Confirm_PaymentSnapshotMismatch_RejectsWithoutChangingOrder()
    {
        var order = CreateOrder();
        order.InventoryReserved(Guid.NewGuid(), Created.AddMinutes(1));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            order.Confirm(Guid.NewGuid(), 9m, "CNY", PriceHash, Created.AddMinutes(2)));

        Assert.AreEqual(OrderStatus.AwaitingPayment, order.Status);
        Assert.IsNull(order.PaymentId);
        Assert.AreEqual(1L, order.Version);
    }

    [TestMethod]
    public void Fulfillment_ConfirmedOrder_TransitionsInSequenceAndIgnoresExactReplay()
    {
        var order = CreateOrder();
        order.InventoryReserved(Guid.NewGuid(), Created.AddMinutes(1));
        order.Confirm(Guid.NewGuid(), 10m, "cny", PriceHash, Created.AddMinutes(2));

        Assert.IsTrue(order.Ship("ship-key", new string('B', 64), " sf ", "Shunfeng", " track-1 ",
            "packed", "operator", Created.AddMinutes(3)));
        var versionAfterShip = order.Version;
        Assert.IsFalse(order.Ship("ship-key", new string('B', 64), "SF", "Shunfeng", "TRACK-1",
            "packed", "operator", Created.AddMinutes(4)));
        Assert.AreEqual(versionAfterShip, order.Version);
        Assert.IsTrue(order.MarkInTransit("transit-key", new string('C', 64), null, "operator", Created.AddMinutes(5)));
        Assert.IsTrue(order.MarkDelivered("delivery-key", new string('D', 64), null, "operator", Created.AddMinutes(6)));

        Assert.AreEqual(OrderStatus.Confirmed, order.Status);
        Assert.AreEqual(OrderFulfillmentStatus.Delivered, order.FulfillmentStatus);
        Assert.AreEqual("SF", order.CarrierCode);
        Assert.AreEqual("TRACK-1", order.TrackingNumber);
        Assert.AreEqual(6L, order.Version);
        Assert.AreEqual(5, order.FulfillmentStatusHistory.Count);
    }

    [TestMethod]
    public void Ship_ReusedIdempotencyKeyWithDifferentFingerprint_ThrowsConflict()
    {
        var order = CreateOrder();
        order.InventoryReserved(Guid.NewGuid(), Created.AddMinutes(1));
        order.Confirm(Guid.NewGuid(), 10m, "CNY", PriceHash, Created.AddMinutes(2));
        order.Ship("ship-key", new string('B', 64), "SF", "Shunfeng", "TRACK-1",
            null, "operator", Created.AddMinutes(3));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            order.Ship("ship-key", new string('C', 64), "SF", "Shunfeng", "TRACK-2",
                null, "operator", Created.AddMinutes(4)));

        Assert.AreEqual("TRACK-1", order.TrackingNumber);
        Assert.AreEqual(4L, order.Version);
    }

    [TestMethod]
    public void Create_WithOnlyBasketId_RejectsIncompleteCheckoutLink()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateOrder(Guid.NewGuid(), null));
    }

    private static Order CreateOrder(Guid? basketId = null, long? basketVersion = null) => new(
        Guid.NewGuid(), "buyer-1", "order-key", new string('F', 64), "Alipay",
        new ShippingAddressSnapshot(null, "Buyer", "13800000000", "CN", "Province", "City",
            "District", "Street", null, null),
        [new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "SKU-1", "Product", "Variant", "{}", 1, 10m, 0m)],
        [], "CNY", basketId, basketVersion, Guid.NewGuid(), PriceHash,
        Created.AddMinutes(20), Created);
}
