using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.BasketService.Application;
using MSA.BasketService.Application.Abstractions;
using MSA.BasketService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class BasketApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly IOptions<BasketOptions> Options = Microsoft.Extensions.Options.Options.Create(new BasketOptions());

    [TestMethod]
    public async Task AddItem_UnknownProduct_DoesNotCreateEmptyBasket()
    {
        var baskets = new Mock<IBasketRepository>();
        var mutationLock = new Mock<IBasketMutationLock>();
        var handler = new AddBasketItemCommandHandler(
            baskets.Object, Mock.Of<IBasketProductCatalog>(), mutationLock.Object, Options, new TestClock(Now));

        await Assert.ThrowsExactlyAsync<ProductUnavailableException>(() => handler.Handle(
            new AddBasketItemCommand("buyer-1", "sku-1", 1, null), CancellationToken.None));

        mutationLock.Verify(value => value.AcquireAsync("buyer-1", It.IsAny<CancellationToken>()), Times.Once);
        baskets.Verify(value => value.Add(It.IsAny<Basket>()), Times.Never);
    }

    [TestMethod]
    public async Task AddItem_FirstItem_CreatesBasketAtVersionOne()
    {
        var baskets = new Mock<IBasketRepository>();
        Basket? created = null;
        baskets.Setup(value => value.Add(It.IsAny<Basket>())).Callback<Basket>(value => created = value);
        var products = new Mock<IBasketProductCatalog>();
        products.Setup(value => value.FindBySkuAsync("sku-1", It.IsAny<CancellationToken>())).ReturnsAsync(Selection());
        var handler = new AddBasketItemCommandHandler(
            baskets.Object, products.Object, Mock.Of<IBasketMutationLock>(), Options, new TestClock(Now));

        var result = await handler.Handle(new AddBasketItemCommand("buyer-1", "sku-1", 2, 0), CancellationToken.None);

        Assert.IsNotNull(created);
        Assert.AreEqual(created.Id, result.BasketId);
        Assert.AreEqual(1L, result.Version);
        Assert.AreEqual(2, created.Items.Single().Quantity);
    }

    [TestMethod]
    public async Task AddItem_MissingBasketWithNonZeroExpectedVersion_ReportsConflict()
    {
        var products = new Mock<IBasketProductCatalog>();
        products.Setup(value => value.FindBySkuAsync("sku-1", It.IsAny<CancellationToken>())).ReturnsAsync(Selection());
        var baskets = new Mock<IBasketRepository>();
        var handler = new AddBasketItemCommandHandler(
            baskets.Object, products.Object, Mock.Of<IBasketMutationLock>(), Options, new TestClock(Now));

        var conflict = await Assert.ThrowsExactlyAsync<BasketVersionConflictException>(() => handler.Handle(
            new AddBasketItemCommand("buyer-1", "sku-1", 1, 3), CancellationToken.None));

        Assert.AreEqual(0L, conflict.ActualVersion);
        baskets.Verify(value => value.Add(It.IsAny<Basket>()), Times.Never);
    }

    [TestMethod]
    public async Task RemoveAndClear_MissingBasket_AreIdempotentOnlyForVersionZero()
    {
        var baskets = Mock.Of<IBasketRepository>();
        var remove = new RemoveBasketItemCommandHandler(baskets, Mock.Of<IBasketMutationLock>(), new TestClock(Now));
        var clear = new ClearBasketCommandHandler(baskets, Mock.Of<IBasketMutationLock>(), new TestClock(Now));

        var removed = await remove.Handle(new RemoveBasketItemCommand("buyer-1", "sku-1", 0), CancellationToken.None);
        var cleared = await clear.Handle(new ClearBasketCommand("buyer-1", null), CancellationToken.None);

        Assert.AreEqual(new BasketMutationResult(Guid.Empty, 0), removed);
        Assert.AreEqual(new BasketMutationResult(Guid.Empty, 0), cleared);
        await Assert.ThrowsExactlyAsync<BasketVersionConflictException>(() =>
            remove.Handle(new RemoveBasketItemCommand("buyer-1", "sku-1", 2), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<BasketVersionConflictException>(() =>
            clear.Handle(new ClearBasketCommand("buyer-1", 1), CancellationToken.None));
    }

    [TestMethod]
    public async Task ReplaceCoupons_EmptyListOnMissingBasket_DoesNotCreateBasket()
    {
        var baskets = new Mock<IBasketRepository>();
        var handler = new ReplaceBasketCouponsCommandHandler(
            baskets.Object, Mock.Of<IBasketMutationLock>(), Options, new TestClock(Now));

        var result = await handler.Handle(new ReplaceBasketCouponsCommand("buyer-1", [], null), CancellationToken.None);
        await handler.Handle(new ReplaceBasketCouponsCommand("buyer-1", ["save10"], null), CancellationToken.None);

        Assert.AreEqual(0L, result.Version);
        baskets.Verify(value => value.Add(It.Is<Basket>(basket => basket.Coupons.Single().Code == "SAVE10")), Times.Once);
    }

    [TestMethod]
    public async Task SetQuantity_MissingBasket_ThrowsNotFound()
    {
        var handler = new SetBasketItemQuantityCommandHandler(
            Mock.Of<IBasketRepository>(), Mock.Of<IBasketProductCatalog>(), Mock.Of<IBasketMutationLock>(), Options, new TestClock(Now));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => handler.Handle(
            new SetBasketItemQuantityCommand("buyer-1", "sku-1", 1, null), CancellationToken.None));
    }

    [TestMethod]
    public void Validators_EnforceConfiguredLimits()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new BasketOptions { MaximumLineQuantity = 5, MaximumCouponCodes = 1 });

        Assert.IsFalse(new AddBasketItemCommandValidator(options).Validate(new AddBasketItemCommand("buyer-1", "SKU", 6, null)).IsValid);
        Assert.IsFalse(new AddBasketItemCommandValidator(options).Validate(new AddBasketItemCommand("buyer-1", "SKU", 1, -1)).IsValid);
        Assert.IsTrue(new AddBasketItemCommandValidator(options).Validate(new AddBasketItemCommand("buyer-1", "SKU", 5, 0)).IsValid);
        Assert.IsFalse(new ReplaceBasketCouponsCommandValidator(options)
            .Validate(new ReplaceBasketCouponsCommand("buyer-1", ["A", "B"], null)).IsValid);
    }

    [TestMethod]
    public void ProductSnapshot_InvalidNewerEvent_LeavesProjectionUntouched()
    {
        var snapshot = new BasketProductSnapshot(Guid.NewGuid(), Guid.NewGuid(), "sku-1", "Product", "Variant",
            "{\"color\":\"red\"}", 12m, "CNY", true, 1, Now);

        Assert.ThrowsExactly<ArgumentException>(() =>
            snapshot.Apply("sku-1", "Renamed", "Renamed variant", "{}", 13m, "RMB1", true, 2, Now.AddMinutes(1)));

        Assert.AreEqual("Product", snapshot.ProductName);
        Assert.AreEqual("Variant", snapshot.VariantName);
        Assert.AreEqual("{\"color\":\"red\"}", snapshot.AttributesJson);
        Assert.AreEqual(1L, snapshot.SourceRevision);
    }

    [TestMethod]
    public void ProductSnapshot_NewerRevision_NormalizesAndApplies()
    {
        var snapshot = new BasketProductSnapshot(Guid.NewGuid(), Guid.NewGuid(), "sku-1", "Product", "Variant",
            "{}", 12m, "CNY", true, 1, Now);

        Assert.IsTrue(snapshot.Apply(" sku-1 ", " Renamed ", "Variant", " ", 9.995m, "usd", false, 2, Now));

        Assert.AreEqual("SKU-1", snapshot.Sku);
        Assert.AreEqual("Renamed", snapshot.ProductName);
        Assert.AreEqual("{}", snapshot.AttributesJson);
        Assert.AreEqual(10.00m, snapshot.Price);
        Assert.AreEqual("USD", snapshot.Currency);
        Assert.IsFalse(snapshot.ToSelection().IsAvailable);
    }

    [TestMethod]
    public void Basket_NoOpRemoveAndClear_DoNotAdvanceVersionOrRequireFreshVersion()
    {
        var basket = new Basket(Guid.NewGuid(), " buyer-1 ", Now);

        Assert.IsFalse(basket.RemoveItem("missing", 99, Now.AddMinutes(1)));
        Assert.IsFalse(basket.Clear(99, Now.AddMinutes(1)));
        basket.AddOrIncreaseItem(Selection(), 1, 1, 10, 0, Now.AddMinutes(2));

        Assert.AreEqual("buyer-1", basket.UserId);
        Assert.AreEqual(1L, basket.Version);
        Assert.ThrowsExactly<BasketRuleException>(() => basket.AddOrIncreaseItem(
            Selection("sku-2"), 1, 1, 10, 1, Now.AddMinutes(3)));
        Assert.ThrowsExactly<BasketRuleException>(() => basket.AddOrIncreaseItem(
            Selection(), 10, 1, 10, 1, Now.AddMinutes(3)));
        Assert.IsTrue(basket.Clear(1, Now.AddMinutes(-5)));
        Assert.AreEqual(Now.AddMinutes(2), basket.UpdatedOnUtc, "Out-of-order timestamps must not move the basket clock backwards.");
    }

    private static ProductSelection Selection(string sku = "sku-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), sku, "Product", "Variant", "{}", 10m, "CNY", true, 1);
}
