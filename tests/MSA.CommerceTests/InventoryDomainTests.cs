using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.InventoryService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class InventoryDomainTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void StockItem_ReserveCommitRelease_PreservesAvailableBalance()
    {
        var stock = new StockItem(Guid.NewGuid(), " sku-1 ", 10);

        stock.Reserve(6);
        stock.Commit(4);
        stock.Release(2);

        Assert.AreEqual("SKU-1", stock.Sku);
        Assert.AreEqual(6, stock.OnHand);
        Assert.AreEqual(0, stock.Reserved);
        Assert.AreEqual(6, stock.Available);
        Assert.AreEqual(3L, stock.Version);
    }

    [TestMethod]
    public void StockItem_InsufficientOrReservedAdjustment_RejectsWithoutChangingBalance()
    {
        var stock = new StockItem(Guid.NewGuid(), "SKU-1", 5);
        stock.Reserve(4);

        Assert.ThrowsExactly<InvalidOperationException>(() => stock.Reserve(2));
        Assert.ThrowsExactly<InvalidOperationException>(() => stock.Adjust(-2));

        Assert.AreEqual(5, stock.OnHand);
        Assert.AreEqual(4, stock.Reserved);
        Assert.AreEqual(1, stock.Available);
        Assert.AreEqual(1L, stock.Version);
    }

    [TestMethod]
    public void Allocation_TimeWindowAndClose_ExcludeEndAndReleaseOnlyUnreservedStock()
    {
        var allocation = CreateAllocation();
        var end = Start.AddMinutes(10);

        Assert.IsFalse(allocation.AcceptsOrders(Start.AddTicks(-1)));
        Assert.IsTrue(allocation.AcceptsOrders(Start));
        Assert.IsFalse(allocation.AcceptsOrders(end));
        Assert.IsTrue(allocation.TryReserve(4, Start));
        Assert.IsFalse(allocation.TryReserve(1, end));
        Assert.AreEqual(6, allocation.Close(Start.AddMinutes(5)));
        Assert.AreEqual(0, allocation.Close(Start.AddMinutes(6)));
        allocation.Release(4);

        Assert.AreEqual(0, allocation.ReservedQuantity);
        Assert.AreEqual(10, allocation.ReleasedQuantity);
        Assert.AreEqual(0, allocation.AvailableQuantity);
        Assert.AreEqual(InventoryAllocationStatus.Closed, allocation.Status);
        Assert.AreEqual(3L, allocation.Version);
    }

    [TestMethod]
    public void AllocationBuyer_LimitCountsSoldAndReservedUntilReleased()
    {
        var buyer = new InventoryAllocationBuyer(Guid.NewGuid(), " buyer ", 2, 3, Start);
        buyer.Commit(1, Start.AddMinutes(1));

        Assert.ThrowsExactly<InvalidOperationException>(() => buyer.Reserve(2, 3, Start.AddMinutes(2)));
        buyer.Release(1, Start.AddMinutes(2));
        buyer.Reserve(2, 3, Start.AddMinutes(3));

        Assert.AreEqual("buyer", buyer.UserId);
        Assert.AreEqual(2, buyer.ReservedQuantity);
        Assert.AreEqual(1, buyer.SoldQuantity);
        Assert.AreEqual(1, buyer.ReleasedQuantity);
        Assert.AreEqual(4L, buyer.Version);
    }

    [TestMethod]
    public void Reservation_TerminalTransition_IsIdempotentAndCannotCommitAfterRelease()
    {
        var reservation = new InventoryReservation(Guid.NewGuid(), Guid.NewGuid(),
            [new InventoryReservationLine("sku-1", 2)], Start, Start.AddMinutes(20));

        Assert.IsTrue(reservation.TryRelease(Start.AddMinutes(1), "cancelled"));
        Assert.IsFalse(reservation.TryRelease(Start.AddMinutes(2), "duplicate"));
        Assert.IsFalse(reservation.TryCommit(Start.AddMinutes(3)));
        Assert.AreEqual("cancelled", reservation.TerminalReason);
        Assert.AreEqual(ReservationStatus.Released, reservation.Status);
        Assert.AreEqual(1L, reservation.Version);
    }

    private static InventoryAllocation CreateAllocation() => new(
        Guid.NewGuid(), Guid.NewGuid(), "SKU-1", 10, Start, Start.AddMinutes(10), 3, Start.AddMinutes(-1));
}
