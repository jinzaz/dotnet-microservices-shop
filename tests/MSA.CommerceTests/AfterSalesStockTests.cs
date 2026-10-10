using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Contracts;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using MSA.InventoryService.Integration;

namespace MSA.CommerceTests;

[TestClass]
public sealed class AfterSalesStockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ApplyAsync_LedgerMakesEveryOperationReplaySafe()
    {
        await using var db = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.StockItems.Add(new StockItem(Guid.NewGuid(), "SKU-1", 5));
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var requestId = Guid.NewGuid();

        Assert.IsTrue((await Apply(db, requestId, AfterSalesStockOperations.Reserve, 2))!.Reserved);
        Assert.IsTrue((await Apply(db, requestId, AfterSalesStockOperations.Reserve, 2))!.Reserved);
        AssertStock(db, onHand: 5, reserved: 2);

        Assert.IsNull(await Apply(db, requestId, AfterSalesStockOperations.Commit, 2));
        Assert.IsNull(await Apply(db, requestId, AfterSalesStockOperations.Commit, 2));
        // 已出库的新品不能再被「关闭释放」还回去。
        Assert.IsNull(await Apply(db, requestId, AfterSalesStockOperations.Release, 2));
        AssertStock(db, onHand: 3, reserved: 0);

        Assert.IsNull(await Apply(db, requestId, AfterSalesStockOperations.Restock, 1));
        Assert.IsNull(await Apply(db, requestId, AfterSalesStockOperations.Restock, 1));
        AssertStock(db, onHand: 4, reserved: 0);
        Assert.AreEqual(3, await db.StockMovements.CountAsync(TestContext.CancellationToken));

        var shortage = await Apply(db, Guid.NewGuid(), AfterSalesStockOperations.Reserve, 10);
        Assert.IsFalse(shortage!.Reserved);
        AssertStock(db, onHand: 4, reserved: 0);
    }

    [TestMethod]
    public async Task ApplyAsync_ReleaseReturnsAHeldReplacementOnce()
    {
        await using var db = new InventoryDbContext(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.StockItems.Add(new StockItem(Guid.NewGuid(), "SKU-1", 5));
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var requestId = Guid.NewGuid();

        await Apply(db, requestId, AfterSalesStockOperations.Reserve, 3);
        await Apply(db, requestId, AfterSalesStockOperations.Release, 3);
        await Apply(db, requestId, AfterSalesStockOperations.Release, 3);

        AssertStock(db, onHand: 5, reserved: 0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Apply(db, requestId, AfterSalesStockOperations.Commit, 3));
    }

    private async Task<AfterSalesStockReservationResultIntegrationEvent?> Apply(InventoryDbContext db, Guid requestId, string operation, int quantity)
    {
        var result = await AfterSalesStock.ApplyAsync(
            db,
            Mock.Of<IInventoryTransactionLock>(),
            new AfterSalesStockRequestedIntegrationEvent
            {
                RequestId = requestId,
                RequestNumber = "A20261010TEST",
                Operation = operation,
                Items = [new AfterSalesStockItemContract("sku-1", quantity)],
                OperatorId = "warehouse"
            },
            Now,
            TestContext.CancellationToken);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        return result;
    }

    private static void AssertStock(InventoryDbContext db, int onHand, int reserved)
    {
        var item = db.StockItems.Single();
        Assert.AreEqual(onHand, item.OnHand);
        Assert.AreEqual(reserved, item.Reserved);
    }
}
