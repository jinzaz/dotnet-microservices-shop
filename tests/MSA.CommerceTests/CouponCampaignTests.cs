using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;

namespace MSA.CommerceTests;

[TestClass]
public sealed class CouponCampaignTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly CouponClaimCounts None = new(0, 0, 0);

    [TestMethod]
    public void Claim_EnforcesEveryLimitWithADistinctReason()
    {
        var campaign = Published(Definition() with { TotalQuantity = 10, DailyQuantity = 5, PerUserLimit = 3, PerUserDailyLimit = 2 });

        AssertRejected(CouponClaimRejection.UserLimitReached, () => Claim(campaign, new CouponClaimCounts(3, 0, 0)));
        AssertRejected(CouponClaimRejection.UserDailyLimitReached, () => Claim(campaign, new CouponClaimCounts(2, 2, 2)));
        AssertRejected(CouponClaimRejection.DailySoldOut, () => Claim(campaign, new CouponClaimCounts(0, 0, 5)));
        AssertRejected(CouponClaimRejection.NotStarted, () => campaign.Claim(None, Now, Now.AddDays(30), Now.AddHours(-2)));
        AssertRejected(CouponClaimRejection.Ended, () => campaign.Claim(None, Now, Now.AddDays(30), Now.AddDays(8)));
        Assert.AreEqual(0, campaign.ClaimedQuantity, "Rejected claims must not consume stock.");

        for (var index = 0; index < 10; index++)
            Claim(campaign, None);
        AssertRejected(CouponClaimRejection.SoldOut, () => Claim(campaign, None));
        Assert.AreEqual(0, campaign.RemainingQuantity);
    }

    [TestMethod]
    public void Claim_DoesNotBumpVersionSoBackOfficeEditsDoNotConflictWithShoppers()
    {
        var campaign = Published(Definition());
        var version = campaign.Version;

        Claim(campaign, None);

        Assert.AreEqual(version, campaign.Version);
        Assert.AreEqual(1, campaign.ClaimedQuantity);
    }

    [TestMethod]
    public void Claim_RelativeValidityStartsNowAndIsClampedToThePromotionEnd()
    {
        var campaign = Published(Definition() with { ValidityMode = CouponValidityMode.RelativeToClaim, ValidFromUtc = null, ValidUntilUtc = null, ValidDays = 7 });
        var claimedAt = Now.AddHours(1);

        var (from, until) = campaign.Claim(None, Now, Now.AddDays(3), claimedAt);

        Assert.AreEqual(claimedAt, from);
        Assert.AreEqual(Now.AddDays(3), until);
    }

    [TestMethod]
    public void Definition_RejectsInconsistentLimitsAndValidity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => New(Definition() with { TotalQuantity = 5, DailyQuantity = 6 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => New(Definition() with { TotalQuantity = 2, PerUserLimit = 3 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => New(Definition() with { PerUserLimit = 1, PerUserDailyLimit = 2 }));
        Assert.ThrowsExactly<ArgumentException>(() => New(Definition() with { ValidDays = 7 }));
        Assert.ThrowsExactly<ArgumentException>(() => New(Definition() with { ValidityMode = CouponValidityMode.RelativeToClaim, ValidDays = null }));
        Assert.ThrowsExactly<ArgumentException>(() => New(Definition() with { ValidFromUtc = Now.AddDays(-3), ValidUntilUtc = Now.AddHours(-1) }));
    }

    [TestMethod]
    public void Lifecycle_OnlyDraftsAreEditableAndStockCannotDropBelowClaimed()
    {
        var campaign = Published(Definition() with { TotalQuantity = 10 });
        Claim(campaign, None);
        Claim(campaign, None);

        Assert.ThrowsExactly<InvalidOperationException>(() => campaign.Revise(Definition(), "operator", Now));
        Assert.ThrowsExactly<InvalidOperationException>(() => campaign.ChangeTotalQuantity(1, "operator", Now));
        campaign.ChangeTotalQuantity(50, "operator", Now);
        campaign.ChangeTotalQuantity(null, "operator", Now);
        Assert.IsNull(campaign.RemainingQuantity);

        campaign.Pause("operator", Now);
        AssertRejected(CouponClaimRejection.Unavailable, () => Claim(campaign, None));
        campaign.Resume("operator", Now);
        campaign.End("operator", Now);
        Assert.ThrowsExactly<InvalidOperationException>(() => campaign.ChangeTotalQuantity(80, "operator", Now));
    }

    [TestMethod]
    public void StorefrontState_MatchesWhatAClaimWouldDo()
    {
        var promotion = ActivePromotion();
        var campaign = Published(Definition() with { TotalQuantity = 1, PerUserLimit = 1 });

        Assert.AreEqual(CouponClaimStates.Claimable, CouponCampaignStore.StateOf(campaign, promotion, 0, 0, 0, Now));
        Assert.AreEqual(CouponClaimStates.NotStarted, CouponCampaignStore.StateOf(campaign, promotion, 0, 0, 0, Now.AddHours(-2)));
        Assert.AreEqual(CouponClaimStates.LimitReached, CouponCampaignStore.StateOf(campaign, promotion, 0, 1, 1, Now));
        Claim(campaign, None);
        Assert.AreEqual(CouponClaimStates.SoldOut, CouponCampaignStore.StateOf(campaign, promotion, 1, 0, 0, Now));
        Assert.AreEqual(CouponClaimStates.Ended, CouponCampaignStore.StateOf(campaign, promotion, 0, 0, 0, Now.AddDays(8)));
    }

    [TestMethod]
    public async Task ClaimHandler_IssuesAnActivePersonalCouponOnTheBusinessDay()
    {
        var promotion = ActivePromotion();
        var campaign = Published(Definition() with { PerUserLimit = 2, UsesPerCoupon = 2 }, promotion.Id);
        var campaigns = new Mock<ICouponCampaignRepository>();
        campaigns.Setup(value => value.FindForUpdateAsync(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        // 17:30 UTC is already the next day in Shanghai, so daily limits must count against 2026-09-17.
        var claimedAt = new DateTimeOffset(2026, 9, 16, 17, 30, 0, TimeSpan.Zero);
        var businessDay = new DateOnly(2026, 9, 17);
        campaigns.Setup(value => value.CountClaimsAsync(campaign.Id, "buyer-1", businessDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CouponClaimCounts(1, 0, 3));
        CouponClaim? claim = null;
        campaigns.Setup(value => value.AddClaim(It.IsAny<CouponClaim>())).Callback<CouponClaim>(value => claim = value);
        var promotions = new Mock<IPromotionRepository>();
        promotions.Setup(value => value.FindAsync(promotion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(promotion);
        Coupon? coupon = null;
        var coupons = new Mock<ICouponRepository>();
        coupons.Setup(value => value.Add(It.IsAny<Coupon>())).Callback<Coupon>(value => coupon = value);
        var snapshots = new Mock<ICatalogSnapshotPublisher>();
        var handler = new ClaimCouponHandler(campaigns.Object, promotions.Object, coupons.Object, snapshots.Object, new TestClock(claimedAt));

        var result = await handler.Handle(new ClaimCouponCommand(campaign.Id, "buyer-1"), CancellationToken.None);

        Assert.IsNotNull(coupon);
        Assert.AreEqual(CouponStatus.Active, coupon.Status);
        Assert.AreEqual("buyer-1", coupon.AssignedUserId);
        Assert.AreEqual(campaign.Id, coupon.CampaignId);
        Assert.AreEqual(2, coupon.PerUserLimit);
        Assert.IsNull(coupon.TotalLimit);
        snapshots.Verify(value => value.Publish(coupon), Times.Once);
        Assert.IsNotNull(claim);
        Assert.AreEqual(businessDay, claim.ClaimDay);
        Assert.AreEqual(coupon.Id, claim.CouponId);
        Assert.AreEqual(2, result.MyClaimedCount);
        Assert.AreEqual(coupon.MaskedCode, result.MaskedCode);
    }

    [TestMethod]
    public async Task ClaimHandler_RejectsWhenThePromotionIsNotActive()
    {
        var promotion = ActivePromotion();
        promotion.Pause("operator", Now);
        var campaign = Published(Definition(), promotion.Id);
        var campaigns = new Mock<ICouponCampaignRepository>();
        campaigns.Setup(value => value.FindForUpdateAsync(campaign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(campaign);
        var promotions = new Mock<IPromotionRepository>();
        promotions.Setup(value => value.FindAsync(promotion.Id, It.IsAny<CancellationToken>())).ReturnsAsync(promotion);
        var coupons = new Mock<ICouponRepository>();
        var handler = new ClaimCouponHandler(campaigns.Object, promotions.Object, coupons.Object,
            Mock.Of<ICatalogSnapshotPublisher>(), new TestClock(Now));

        var rejected = await Assert.ThrowsExactlyAsync<CouponClaimRejectedException>(() =>
            handler.Handle(new ClaimCouponCommand(campaign.Id, "buyer-1"), CancellationToken.None));

        Assert.AreEqual(CouponClaimRejection.Unavailable, rejected.Reason);
        coupons.Verify(value => value.Add(It.IsAny<Coupon>()), Times.Never);
        Assert.AreEqual(0, campaign.ClaimedQuantity);
    }

    [TestMethod]
    public void EnsureFits_RejectsClaimWindowsOutsideThePromotion()
    {
        var promotion = ActivePromotion();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CouponCampaignRules.EnsureFits(promotion, Definition() with { ClaimEndsOnUtc = Now.AddDays(40) }));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            CouponCampaignRules.EnsureFits(promotion, Definition() with { ValidUntilUtc = Now.AddDays(40) }));
        CouponCampaignRules.EnsureFits(promotion, Definition());
    }

    private static CouponCampaignDefinition Definition() => new(
        "满 99 减 20",
        null,
        Now.AddHours(-1),
        Now.AddDays(7),
        100,
        null,
        1,
        null,
        CouponValidityMode.Fixed,
        Now,
        Now.AddDays(14),
        null,
        1,
        true);

    private static CouponCampaign New(CouponCampaignDefinition definition, Guid? promotionId = null) =>
        new(Guid.NewGuid(), promotionId ?? Guid.NewGuid(), definition, "operator", Now.AddHours(-3));

    private static CouponCampaign Published(CouponCampaignDefinition definition, Guid? promotionId = null)
    {
        var campaign = New(definition, promotionId);
        campaign.Publish("operator", Now.AddHours(-2));
        return campaign;
    }

    private static (DateTimeOffset, DateTimeOffset) Claim(CouponCampaign campaign, CouponClaimCounts counts) =>
        campaign.Claim(counts, Now.AddDays(-1), Now.AddDays(30), Now);

    private static void AssertRejected(CouponClaimRejection reason, Action claim) =>
        Assert.AreEqual(reason, Assert.ThrowsExactly<CouponClaimRejectedException>(claim).Reason);

    private static Promotion ActivePromotion()
    {
        var promotion = new Promotion(Guid.NewGuid(), "claim", "Claim", PromotionTriggerType.Coupon,
            Now.AddDays(-1), Now.AddDays(30), 0, PromotionStackPolicy.Stackable, null, "operator", Now.AddDays(-2));
        promotion.ReplaceDefinition(
            Array.Empty<PromotionRuleDefinition>(),
            [new PromotionBenefitDefinition("FixedAmountOff", 1, "{\"amount\":20}", 0)],
            Array.Empty<PromotionTargetDefinition>(),
            "operator",
            Now.AddDays(-2));
        promotion.Publish("operator", Now.AddDays(-2));
        return promotion;
    }
}
