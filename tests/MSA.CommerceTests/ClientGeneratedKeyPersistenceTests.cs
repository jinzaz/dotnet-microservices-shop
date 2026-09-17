using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.BasketService.Domain;
using MSA.BasketService.Infrastructure;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.InventoryService.Infrastructure;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.CommerceTests;

/// <summary>
/// Domain code assigns Guid ids itself. If EF believes those keys are generated on add, a new child reached
/// through a tracked aggregate's navigation is treated as an existing row and saved with UPDATE (0 rows affected).
/// </summary>
[TestClass]
public sealed class ClientGeneratedKeyPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Catalog_AddVariantToLoadedProduct_InsertsVariantInsteadOfUpdating()
    {
        var options = Options<CatalogDbContext>();
        var product = new MSA.CatalogService.Domain.Product(Guid.NewGuid(), Guid.NewGuid(), "SKU-XIAOMI-001",
            "Xiaomi 13 Pro", "Flagship", 6999m, "CNY", "operator", Now);
        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            db.Products.Add(product);
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            var loaded = await db.Products
                .Include(value => value.Variants)
                .Include(value => value.Media)
                .SingleAsync(TestContext.CancellationToken);
            var variant = loaded.AddVariant(Guid.NewGuid(), "SKU-XIAOMI-002", "Xiaomi 13 Pro Premium Edition Black",
                "{\"Color\":\"Black\",\"Version\":\"Premium Edition\"}", null, 7998m, "CNY", true, "operator", Now.AddMinutes(1));
            var media = loaded.AddMedia(Guid.NewGuid(), variant.Id, ProductMediaType.Image, "https://example.test/black.jpg",
                null, null, 0, "operator", Now.AddMinutes(2));
            db.ChangeTracker.DetectChanges();

            Assert.AreEqual(EntityState.Added, db.Entry(variant).State);
            Assert.AreEqual(EntityState.Added, db.Entry(media).State);
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            Assert.AreEqual(2, await db.ProductVariants.CountAsync(TestContext.CancellationToken));
            Assert.AreEqual(1, await db.ProductMedia.CountAsync(TestContext.CancellationToken));
        }
    }

    [TestMethod]
    public async Task Basket_AddItemAndCouponToLoadedBasket_InsertsChildren()
    {
        var options = Options<BasketDbContext>();
        var basket = new Basket(Guid.NewGuid(), "buyer-1", Now);
        await using (var db = new BasketDbContext(options, Mock.Of<IMediator>()))
        {
            db.Baskets.Add(basket);
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new BasketDbContext(options, Mock.Of<IMediator>()))
        {
            var loaded = await db.Baskets
                .Include(value => value.Items)
                .Include(value => value.Coupons)
                .SingleAsync(TestContext.CancellationToken);
            loaded.AddOrIncreaseItem(
                new ProductSelection(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", "Product", "Variant", "{}", 10m, "CNY", true, 1),
                1, 10, 10, 0, Now.AddMinutes(1));
            loaded.ReplaceCouponCodes(["SAVE10"], 5, 1, Now.AddMinutes(2));

            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new BasketDbContext(options, Mock.Of<IMediator>()))
        {
            Assert.AreEqual(1, await db.BasketItems.CountAsync(TestContext.CancellationToken));
            Assert.AreEqual(1, await db.BasketCoupons.CountAsync(TestContext.CancellationToken));
        }
    }

    [TestMethod]
    public async Task Order_StateTransitionOnLoadedOrder_InsertsHistoryRows()
    {
        var options = Options<OrderDbContext>();
        var priceHash = new string('A', 64);
        var order = new Order(Guid.NewGuid(), "buyer-1", "order-key", new string('F', 64), "Alipay",
            new ShippingAddressSnapshot(null, "Buyer", "13800000000", "CN", "Province", "City", "District", "Street", null, null),
            [new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "SKU-1", "Product", "Variant", "{}", 1, 10m, 10m)],
            [], "CNY", null, null, Guid.NewGuid(), priceHash, Now.AddMinutes(20), Now);
        await using (var db = new OrderDbContext(options, Mock.Of<IMediator>()))
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new OrderDbContext(options, Mock.Of<IMediator>()))
        {
            var loaded = await db.Orders
                .Include(value => value.StatusHistory)
                .Include(value => value.FulfillmentStatusHistory)
                .SingleAsync(TestContext.CancellationToken);
            loaded.InventoryReserved(Guid.NewGuid(), Now.AddMinutes(1));
            loaded.Confirm(Guid.NewGuid(), 10m, "CNY", priceHash, Now.AddMinutes(2));

            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new OrderDbContext(options, Mock.Of<IMediator>()))
        {
            var saved = await db.Orders
                .Include(value => value.StatusHistory)
                .Include(value => value.FulfillmentStatusHistory)
                .SingleAsync(TestContext.CancellationToken);
            Assert.HasCount(3, saved.StatusHistory);
            Assert.HasCount(2, saved.FulfillmentStatusHistory);
        }
    }

    [TestMethod]
    public void CommerceModels_NeverTreatClientAssignedGuidKeysAsStoreGenerated()
    {
        var models = new (string Service, IModel Model)[]
        {
            ("Catalog", Model(new CatalogDbContext(Options<CatalogDbContext>(), Mock.Of<IMediator>()))),
            ("Basket", Model(new BasketDbContext(Options<BasketDbContext>(), Mock.Of<IMediator>()))),
            ("Inventory", Model(new InventoryDbContext(Options<InventoryDbContext>(), Mock.Of<IMediator>()))),
            ("Order", Model(new OrderDbContext(Options<OrderDbContext>(), Mock.Of<IMediator>())))
        };

        var generatedGuidKeys = models
            .SelectMany(item => item.Model.GetEntityTypes()
                .Select(entityType => (item.Service, EntityType: entityType, Key: entityType.FindPrimaryKey())))
            .Where(item => item.Key is { Properties.Count: 1 }
                && item.Key.Properties[0].ClrType == typeof(Guid)
                && item.Key.Properties[0].ValueGenerated != ValueGenerated.Never)
            .Select(item => $"{item.Service}:{item.EntityType.DisplayName()}")
            .ToArray();

        Assert.IsEmpty(generatedGuidKeys, string.Join(", ", generatedGuidKeys));
    }

    private static IModel Model(DbContext db)
    {
        using (db)
            return db.Model;
    }

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
}
