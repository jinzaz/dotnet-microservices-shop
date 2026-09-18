using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Contracts;
using MSA.OrderService.Application.Coupons;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.CommerceTests;

[TestClass]
public sealed class CouponPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid AutomaticPromotionId = Guid.NewGuid();
    private static readonly Guid CodePromotionId = Guid.NewGuid();
    private static readonly Guid WalletPromotionId = Guid.NewGuid();
    private static readonly Guid PublicCouponId = Guid.NewGuid();
    private static readonly Guid WalletCouponId = Guid.NewGuid();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Quote_ExplainsEachCouponAndHintsAtUnmetAutomaticPromotion()
    {
        await using var db = await SeedAsync();

        var quote = await Handler(db).Handle(
            new QuoteOrderQuery("buyer-1", [new QuoteItem("sku-1", 2)], ["save10abc", "NOPE1234"], [WalletCouponId]),
            TestContext.CancellationToken);

        Assert.AreEqual(200m, quote.Subtotal);
        // 10% of 200 = 20 from the typed code, then a fixed 20 from the wallet coupon.
        Assert.AreEqual(40m, quote.DiscountTotal);
        Assert.AreEqual(160m, quote.Total);
        var byKey = quote.Coupons.ToDictionary(coupon => coupon.Key);
        Assert.IsTrue(byKey["SAVE10ABC"].Applied);
        Assert.AreEqual(nameof(CouponRejectionReason.Unknown), byKey["NOPE1234"].Reason);
        Assert.IsTrue(byKey[WalletCouponId.ToString()].Applied);
        var hint = quote.Opportunities.Single();
        Assert.AreEqual(AutomaticPromotionId, hint.PromotionId);
        Assert.AreEqual(100m, hint.MissingAmount, "The automatic promotion needs a 300 subtotal.");
    }

    [TestMethod]
    public async Task Quote_WalletCouponOfAnotherBuyerOrUsedUp_IsRejectedWithReason()
    {
        await using var db = await SeedAsync(consumedWalletRedemption: true);

        var mine = await Handler(db).Handle(
            new QuoteOrderQuery("buyer-1", [new QuoteItem("SKU-1", 1)], null, [WalletCouponId]),
            TestContext.CancellationToken);
        var theirs = await Handler(db).Handle(
            new QuoteOrderQuery("buyer-2", [new QuoteItem("SKU-1", 1)], null, [WalletCouponId]),
            TestContext.CancellationToken);

        Assert.AreEqual(nameof(CouponRejectionReason.UsageLimitReached), mine.Coupons.Single().Reason);
        Assert.AreEqual(0m, mine.DiscountTotal);
        Assert.AreEqual(nameof(CouponRejectionReason.Unknown), theirs.Coupons.Single().Reason);
    }

    [TestMethod]
    public async Task Pricing_ForeignWalletCoupon_ThrowsTypedRejection()
    {
        await using var db = await SeedAsync();
        var pricing = Pricing(db);
        var product = await db.ProductProjections.SingleAsync(TestContext.CancellationToken);

        var rejection = await Assert.ThrowsExactlyAsync<CouponRejectedException>(() => pricing.CalculateAsync(
            "buyer-2", [new PricingItem(product, 1)], new CouponSelection([], [WalletCouponId]), Now, TestContext.CancellationToken));

        Assert.AreEqual(CouponRejectionReason.Unknown, rejection.Reason);
    }

    [TestMethod]
    public async Task Wallet_ListsIssuedAndRedeemedCouponsWithStatus()
    {
        await using var db = await SeedAsync(consumedWalletRedemption: true, publicRedemption: true);

        var wallet = await new GetMyCouponsQueryHandler(db, new TestClock(Now))
            .Handle(new GetMyCouponsQuery("buyer-1"), TestContext.CancellationToken);

        Assert.HasCount(2, wallet);
        var issued = wallet.Single(coupon => coupon.CouponId == WalletCouponId);
        Assert.IsTrue(issued.IsAssigned);
        Assert.AreEqual(WalletCouponStatus.Used, issued.Status);
        var redeemed = wallet.Single(coupon => coupon.CouponId == PublicCouponId);
        Assert.IsFalse(redeemed.IsAssigned);
        Assert.AreEqual(WalletCouponStatus.Available, redeemed.Status, "The public code allows three uses per buyer.");
        Assert.AreEqual("PercentageOff", redeemed.Benefits.Single().BenefitType);
    }

    private static OrderPricingService Pricing(OrderDbContext db) =>
        new(db, [new MinimumOrderAmountRule(), new MinimumItemQuantityRule()], [new PercentageOffBenefit(), new FixedAmountOffBenefit()]);

    private static QuoteOrderQueryHandler Handler(OrderDbContext db) => new(db, Pricing(db), new TestClock(Now));

    private async Task<OrderDbContext> SeedAsync(bool consumedWalletRedemption = false, bool publicRedemption = false)
    {
        var db = new OrderDbContext(
            new DbContextOptionsBuilder<OrderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Mock.Of<IMediator>());
        db.ProductProjections.Add(new OrderProductProjection(
            Guid.NewGuid(), Guid.NewGuid(), "SKU-1", "Mug", "White", "{}", 100m, "CNY", true, null, 1, Now.AddDays(-1)));
        db.PromotionProjections.AddRange(
            Promotion(AutomaticPromotionId, "Automatic", "Spend 300 save 30",
                [new PromotionRuleContract("MinimumOrderAmount", 1, """{"amount":300}""")],
                new PromotionBenefitContract("FixedAmountOff", 1, """{"amount":30}"""), priority: 10),
            Promotion(CodePromotionId, "Coupon", "10% off", [], new PromotionBenefitContract("PercentageOff", 1, """{"percentage":10}"""), priority: 5),
            Promotion(WalletPromotionId, "Coupon", "Welcome 20", [], new PromotionBenefitContract("FixedAmountOff", 1, """{"amount":20}"""), priority: 1));
        db.CouponProjections.AddRange(
            Coupon(PublicCouponId, CodePromotionId, "SAVE10ABC", assignedUserId: null, perUserLimit: 3),
            Coupon(WalletCouponId, WalletPromotionId, "WELCOME-XYZ9", assignedUserId: "buyer-1", perUserLimit: 1));
        if (consumedWalletRedemption)
            db.CouponRedemptions.Add(Redemption(WalletCouponId, "****XYZ9"));
        if (publicRedemption)
            db.CouponRedemptions.Add(Redemption(PublicCouponId, "****0ABC"));
        await db.SaveChangesAsync(TestContext.CancellationToken);
        db.ChangeTracker.Clear();
        return db;
    }

    private static OrderCouponRedemption Redemption(Guid couponId, string masked)
    {
        var redemption = new OrderCouponRedemption(Guid.NewGuid(), Guid.NewGuid(), couponId, "buyer-1", null, masked, 20m, Now.AddDays(1), Now.AddDays(-1));
        redemption.Consume(Now.AddDays(-1));
        return redemption;
    }

    private static PromotionProjection Promotion(
        Guid id, string trigger, string name, IReadOnlyList<PromotionRuleContract> rules, PromotionBenefitContract benefit, int priority) =>
        new(id, $"P-{id:N}", name, trigger, "Active", Now.AddDays(-7), Now.AddDays(7), priority, "Stackable", null, 1, "checksum",
            JsonSerializer.Serialize(rules), JsonSerializer.Serialize(new[] { benefit }), "[]", Now.AddDays(-7));

    private static CouponProjection Coupon(Guid id, Guid promotionId, string code, string? assignedUserId, int perUserLimit) =>
        new(id, promotionId, CouponCode.Digest(code), $"****{code[^4..]}", "Active", Now.AddDays(-7), Now.AddDays(7),
            assignedUserId, null, perUserLimit, "SingleRow", 1, [], Now.AddDays(-7));
}
