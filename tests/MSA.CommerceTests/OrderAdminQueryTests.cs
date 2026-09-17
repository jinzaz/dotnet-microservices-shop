using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Application.GetOrdersSummary;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.CommerceTests;

[TestClass]
public sealed class OrderAdminQueryTests
{
    // 2026-09-17 01:30 UTC is 09:30 on the same day in UTC+8 but still 2026-09-16 in UTC-5.
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 1, 30, 0, TimeSpan.Zero);
    private static readonly string PriceHash = new('A', 64);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Summary_BucketsCreatedAndPaidOrdersByOperatorLocalDay()
    {
        await using var db = await SeedAsync(
            Order("SKU-1", Now.AddHours(-2), confirm: true),              // 2026-09-16 23:30 UTC → 09-17 07:30 local
            Order("SKU-2", Now.AddHours(-20), confirm: false),            // 09-16 05:30 UTC → 09-16 13:30 local
            Order("SKU-3", Now.AddDays(-9), confirm: true));              // outside the 7-day window

        var summary = await new GetOrdersSummaryQueryHandler(db, new TestClock(Now))
            .Handle(new GetOrdersSummaryQuery(7, 480), TestContext.CancellationToken);

        Assert.AreEqual(3, summary.TotalOrders);
        Assert.HasCount(7, summary.Trend);
        var today = summary.Trend[^1];
        var yesterday = summary.Trend[^2];
        Assert.AreEqual(new DateOnly(2026, 9, 17), today.Date);
        Assert.AreEqual(1, today.OrderCount);
        Assert.AreEqual(1, today.PaidOrderCount);
        Assert.AreEqual(10m, today.PaidAmounts.Single(item => item.Currency == "CNY").Amount);
        Assert.AreEqual(1, yesterday.OrderCount);
        Assert.AreEqual(0, yesterday.PaidOrderCount);
        Assert.AreEqual(2, summary.FulfillmentStatuses.Single(item => item.Status == nameof(OrderFulfillmentStatus.AwaitingShipment)).Count);
        Assert.AreEqual(1, summary.FulfillmentStatuses.Single(item => item.Status == nameof(OrderFulfillmentStatus.PendingPayment)).Count);
        Assert.AreEqual(0, summary.FulfillmentStatuses.Single(item => item.Status == nameof(OrderFulfillmentStatus.Delivered)).Count,
            "Every status is reported so the dashboard can render stable chips.");
    }

    [TestMethod]
    public async Task Summary_UsesOperatorOffsetForTodayBoundary()
    {
        await using var db = await SeedAsync(Order("SKU-1", Now.AddHours(-2), confirm: false));

        var summary = await new GetOrdersSummaryQueryHandler(db, new TestClock(Now))
            .Handle(new GetOrdersSummaryQuery(1, -300), TestContext.CancellationToken);

        Assert.AreEqual(new DateOnly(2026, 9, 16), summary.Trend.Single().Date);
        Assert.AreEqual(1, summary.Trend.Single().OrderCount);
    }

    [TestMethod]
    public async Task Summary_RejectsUnsupportedWindow()
    {
        await using var db = await SeedAsync();
        var handler = new GetOrdersSummaryQueryHandler(db, new TestClock(Now));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => handler.Handle(new GetOrdersSummaryQuery(0, 0), TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => handler.Handle(new GetOrdersSummaryQuery(7, 15 * 60), TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task AdminSearch_MatchesOrderIdNumberFragmentPhoneAndIsIgnoredForBuyers()
    {
        var first = Order("SKU-1", Now.AddHours(-1), confirm: false, phone: "13800000001");
        var second = Order("SKU-2", Now.AddHours(-2), confirm: false, phone: "13900000002");
        await using var db = await SeedAsync(first, second);
        var handler = new GetOrdersQueryHandler(db);

        async Task<Guid[]> Search(string search, bool admin = true) =>
            (await handler.Handle(new GetOrdersQuery(admin ? string.Empty : "buyer-1", admin, null, null, 0, 50, search), TestContext.CancellationToken))
                .Select(order => order.Id).ToArray();

        CollectionAssert.AreEqual(new[] { second.Id }, await Search(second.Id.ToString()));
        CollectionAssert.AreEqual(new[] { first.Id }, await Search(first.OrderNumber[^12..].ToUpperInvariant()));
        CollectionAssert.AreEqual(new[] { second.Id }, await Search(" 13900000002 "));
        Assert.HasCount(2, await Search("13900000002", admin: false), "Buyers cannot use admin search to narrow or probe orders.");
    }

    private async Task<OrderDbContext> SeedAsync(params Order[] orders)
    {
        var db = new OrderDbContext(
            new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        db.ChangeTracker.Clear();
        return db;
    }

    private static Order Order(string sku, DateTimeOffset createdOnUtc, bool confirm, string phone = "13800000000")
    {
        var order = new Order(Guid.NewGuid(), "buyer-1", Guid.NewGuid().ToString("N"), new string('F', 64), "Alipay",
            new ShippingAddressSnapshot(null, "Buyer", phone, "CN", "Province", "City", "District", "Street", null, null),
            [new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, sku, "Product", "Variant", "{}", 1, 10m, 10m)],
            [], "CNY", null, null, Guid.NewGuid(), PriceHash, createdOnUtc.AddMinutes(20), createdOnUtc);
        if (confirm)
        {
            order.InventoryReserved(Guid.NewGuid(), createdOnUtc.AddMinutes(1));
            order.Confirm(Guid.NewGuid(), 10m, "CNY", PriceHash, createdOnUtc.AddMinutes(2));
        }
        return order;
    }
}
