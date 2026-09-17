using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.BasketService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class BasketDomainTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AddOrIncreaseItem_RefreshesSelectionSnapshotAndAdvancesVersion()
    {
        var basket = CreateBasket();
        basket.AddOrIncreaseItem(Selection(10m, 1), 2, 200, 999, 0, Created.AddMinutes(1));

        basket.AddOrIncreaseItem(Selection(12m, 2), 3, 200, 999, 1, Created.AddMinutes(2));

        var item = basket.Items.Single();
        Assert.AreEqual(5, item.Quantity);
        Assert.AreEqual(12m, item.UnitPriceAtSelection);
        Assert.AreEqual(2L, item.ProductRevisionAtSelection);
        Assert.AreEqual(2L, basket.Version);
    }

    [TestMethod]
    public void AddOrIncreaseItem_StaleVersionOrUnavailableProduct_RejectsWithoutMutation()
    {
        var basket = CreateBasket();
        basket.AddOrIncreaseItem(Selection(10m, 1), 1, 200, 999, 0, Created.AddMinutes(1));

        var conflict = Assert.ThrowsExactly<BasketVersionConflictException>(() =>
            basket.AddOrIncreaseItem(Selection(10m, 1), 1, 200, 999, 0, Created.AddMinutes(2)));
        Assert.AreEqual(0L, conflict.ExpectedVersion);
        Assert.AreEqual(1L, conflict.ActualVersion);
        Assert.ThrowsExactly<ProductUnavailableException>(() =>
            basket.AddOrIncreaseItem(Selection(10m, 2, available: false), 1, 200, 999, 1, Created.AddMinutes(2)));
        Assert.AreEqual(1, basket.Items.Single().Quantity);
        Assert.AreEqual(1L, basket.Version);
    }

    [TestMethod]
    public void SetItemQuantity_DecreaseWithoutCurrentProduct_AllowsUnavailableAndNoOp()
    {
        var basket = CreateBasket();
        basket.AddOrIncreaseItem(Selection(10m, 1), 3, 200, 999, 0, Created.AddMinutes(1));

        basket.SetItemQuantity("sku-1", 2, null, 999, 1, Created.AddMinutes(2));
        basket.SetItemQuantity("sku-1", 2, null, 999, 0, Created.AddMinutes(3));

        Assert.AreEqual(2, basket.Items.Single().Quantity);
        Assert.AreEqual(2L, basket.Version);
        Assert.ThrowsExactly<ProductUnavailableException>(() =>
            basket.SetItemQuantity("sku-1", 3, null, 999, 2, Created.AddMinutes(4)));
    }

    [TestMethod]
    public void ReplaceCouponCodes_NormalizesDeduplicatesAndSkipsEquivalentUpdate()
    {
        var basket = CreateBasket();

        Assert.IsTrue(basket.ReplaceCouponCodes([" save10 ", "SAVE10", "bonus"], 5, 0, Created.AddMinutes(1)));
        Assert.IsFalse(basket.ReplaceCouponCodes(["BONUS", "save10"], 5, 0, Created.AddMinutes(2)));

        CollectionAssert.AreEqual(new[] { "BONUS", "SAVE10" }, basket.Coupons.Select(coupon => coupon.Code).Order().ToArray());
        Assert.AreEqual(1L, basket.Version);
        Assert.ThrowsExactly<BasketRuleException>(() =>
            basket.ReplaceCouponCodes(["A", "B", "C"], 2, 1, Created.AddMinutes(3)));
    }

    [TestMethod]
    public void ProductSnapshot_OlderRevisionCannotOverwriteNewerSelection()
    {
        var snapshot = new BasketProductSnapshot(Guid.NewGuid(), Guid.NewGuid(), "sku-1", "Product", "Variant",
            "{}", 12m, "CNY", true, 2, Created.AddMinutes(2));

        Assert.IsFalse(snapshot.Apply("sku-1", "Old", "Old", "{}", 1m, "CNY", false, 1, Created.AddMinutes(3)));
        Assert.IsFalse(snapshot.Apply("sku-1", "Duplicate", "Duplicate", "{}", 1m, "CNY", false, 2, Created.AddMinutes(2)));

        Assert.AreEqual("Product", snapshot.ProductName);
        Assert.AreEqual(12m, snapshot.Price);
        Assert.IsTrue(snapshot.IsAvailable);
        Assert.AreEqual(2L, snapshot.ToSelection().SourceRevision);
    }

    private static Basket CreateBasket() => new(Guid.NewGuid(), "buyer-1", Created);

    private static ProductSelection Selection(decimal price, long revision, bool available = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "sku-1", "Product", "Variant", "{}", price, "CNY", available, revision);
}
