using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Contracts;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.CommerceTests;

/// <summary>多个活动命中同一批商品时，优惠只能在各行减剩的金额上继续叠加，并且要分摊回订单行。</summary>
[TestClass]
public sealed class PromotionStackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FixedAmountOffs_OnTheSameSku_NeverExceedThatSku()
    {
        var result = await PriceAsync([Line("A", 10m), Line("B", 200m)],
            Promotion(3, "FixedAmountOff", """{"amount":10}""", "A"),
            Promotion(2, "FixedAmountOff", """{"amount":10}""", "A"),
            Promotion(1, "FixedAmountOff", """{"amount":10}""", "A"));

        Assert.AreEqual(10m, result.DiscountTotal, "Later promotions only see what is left of SKU A.");
        CollectionAssert.AreEqual(new[] { 10m, 0m }, result.LineDiscounts.ToArray());
    }

    [TestMethod]
    public async Task PercentageOffs_OnTheSameSku_Compound()
    {
        var result = await PriceAsync([Line("A", 100m), Line("B", 100m)],
            Promotion(2, "PercentageOff", """{"percentage":50}""", "A"),
            Promotion(1, "PercentageOff", """{"percentage":50}""", "A"));

        Assert.AreEqual(75m, result.DiscountTotal, "50% then 50% of the remaining 50.");
        CollectionAssert.AreEqual(new[] { 75m, 0m }, result.LineDiscounts.ToArray());
    }

    [TestMethod]
    public async Task TargetedAmountThreshold_CountsOnlyEligibleItems()
    {
        var result = await PriceAsync([Line("A", 20m), Line("B", 100m)],
            Promotion(1, "FixedAmountOff", """{"amount":50}""", "A", MinimumAmount(100m)));

        Assert.AreEqual(0m, result.DiscountTotal, "SKU B must not help SKU A reach its threshold.");
        Assert.AreEqual(80m, result.Opportunities.Single().MissingAmount);
    }

    [TestMethod]
    public async Task AmountThreshold_IsJudgedAfterEarlierDiscounts()
    {
        var result = await PriceAsync([Line("A", 100m)],
            Promotion(2, "FixedAmountOff", """{"amount":30}""", rules: MinimumAmount(100m)),
            Promotion(1, "FixedAmountOff", """{"amount":20}""", rules: MinimumAmount(100m)));

        Assert.AreEqual(30m, result.DiscountTotal, "The same 100 cannot satisfy two thresholds.");
        Assert.AreEqual(30m, result.Opportunities.Single().MissingAmount);
    }

    [TestMethod]
    public async Task Discount_IsAllocatedToLinesToTheCent()
    {
        var result = await PriceAsync([Line("A", 10m), Line("B", 10m), Line("C", 10m)],
            Promotion(1, "FixedAmountOff", """{"amount":10}"""));

        CollectionAssert.AreEqual(new[] { 3.34m, 3.33m, 3.33m }, result.LineDiscounts.ToArray());
        Assert.AreEqual(result.DiscountTotal, result.LineDiscounts.Sum());
    }

    [TestMethod]
    public async Task UnknownBenefit_SkipsOnlyThatPromotion()
    {
        var result = await PriceAsync([Line("A", 100m)],
            Promotion(2, "BuyOneGetOne", "{}"),
            Promotion(1, "FixedAmountOff", """{"amount":10}"""));

        Assert.AreEqual(10m, result.DiscountTotal);
    }

    [TestMethod]
    public void OrderLine_KeepsExactLineAmountWhenUnitPriceHasCentResidue()
    {
        var line = new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "A", "A", "A", "{}", 3, 10m, 10m);

        Assert.AreEqual(30m, line.LineSubtotal);
        Assert.AreEqual(20m, line.LineAmount);
        Assert.AreEqual(6.67m, line.UnitPrice);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "A", "A", "A", "{}", 1, 10m, 10.01m));
    }

    private async Task<OrderPricingResult> PriceAsync(PricingItem[] items, params PromotionProjection[] promotions)
    {
        await using var db = new OrderDbContext(
            new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.PromotionProjections.AddRange(promotions);
        await db.SaveChangesAsync(TestContext.CancellationToken);
        var pricing = new OrderPricingService(db,
            [new MinimumOrderAmountRule(), new MinimumItemQuantityRule()],
            [new PercentageOffBenefit(), new FixedAmountOffBenefit()]);
        return await pricing.CalculateAsync("buyer-1", items, CouponSelection.None, Now, TestContext.CancellationToken);
    }

    private static PricingItem Line(string sku, decimal price) =>
        new(new OrderProductProjection(Guid.NewGuid(), Guid.NewGuid(), sku, sku, sku, "{}", price, "CNY", true, null, 1, Now.AddDays(-1)), 1);

    private static PromotionRuleContract[] MinimumAmount(decimal amount) =>
        [new("MinimumOrderAmount", 1, $$"""{"amount":{{amount}}}""")];

    private static PromotionProjection Promotion(
        int priority, string benefitType, string benefitJson, string? targetSku = null, PromotionRuleContract[]? rules = null)
    {
        var id = Guid.NewGuid();
        return new(id, $"P-{id:N}", $"P{priority}", "Automatic", "Active", Now.AddDays(-7), Now.AddDays(7), priority,
            "Stackable", null, 1, "checksum",
            JsonSerializer.Serialize(rules ?? []),
            JsonSerializer.Serialize(new[] { new PromotionBenefitContract(benefitType, 1, benefitJson) }),
            JsonSerializer.Serialize(targetSku is null ? [] : new[] { new PromotionTargetContract("Sku", targetSku) }),
            Now.AddDays(-7));
    }
}
