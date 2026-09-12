using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.OrderService.Domain;

namespace MSA.FlashSaleTests;

[TestClass]
public sealed class OrderTests
{
    [TestMethod]
    public void TryCancel_FirstCancellationSucceedsAndDuplicatePreservesVersionAndReason()
    {
        // Arrange
        var order = new Order(
            Guid.NewGuid(),
            "buyer-001",
            "idempotency-key-001",
            [new OrderLine("SKU-FLASH-001", "Flash sale item", quantity: 1, unitPrice: 99m)],
            "CNY",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero));

        // Act
        var firstCancellation = order.TryCancel("Payment failed.");
        var duplicateCancellation = order.TryCancel("Duplicate delivery must not overwrite the reason.");

        // Assert
        Assert.IsTrue(firstCancellation);
        Assert.IsFalse(duplicateCancellation);
        Assert.AreEqual(OrderStatus.Cancelled, order.Status);
        Assert.AreEqual("Payment failed.", order.CancellationReason);
        Assert.AreEqual(2L, order.Version);
    }
}
