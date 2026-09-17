using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.CatalogService.Application;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.Contracts;
using MSA.EventBusOutbox;

namespace MSA.CommerceTests;

[TestClass]
public sealed class CatalogApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EnsureVersion_MismatchThrowsButMissingExpectationIsLastWriterWins()
    {
        var product = CreateProduct();

        product.EnsureVersion(null);
        product.EnsureVersion(1);
        var conflict = Assert.ThrowsExactly<InvalidOperationException>(() => product.EnsureVersion(7));

        Assert.Contains("Product version mismatch", conflict.Message);
    }

    [TestMethod]
    public async Task UpdateDetails_StaleExpectedVersion_DoesNotChangePublishOrAudit()
    {
        var product = CreateProduct();
        var products = new Mock<IProductRepository>();
        products.Setup(value => value.FindAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        var snapshots = new Mock<ICatalogSnapshotPublisher>();
        var audit = new Mock<ICatalogAuditWriter>();
        var handler = new UpdateProductDetailsHandler(products.Object, snapshots.Object, audit.Object, new TestClock(Now));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.Handle(
            new UpdateProductDetailsCommand(product.Id, "Renamed", "Description", 5, "operator"),
            CancellationToken.None));

        Assert.AreEqual("Product", product.Name);
        snapshots.VerifyNoOtherCalls();
        audit.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateDetails_MissingProduct_ThrowsNotFound()
    {
        var handler = new UpdateProductDetailsHandler(
            Mock.Of<IProductRepository>(),
            Mock.Of<ICatalogSnapshotPublisher>(),
            Mock.Of<ICatalogAuditWriter>(),
            new TestClock(Now));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() => handler.Handle(
            new UpdateProductDetailsCommand(Guid.NewGuid(), "Name", "Description", null, "operator"),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateDraftProduct_InactiveCategoryOrDuplicateSku_IsRejectedBeforeAdd()
    {
        var draftCategory = new Category(Guid.NewGuid(), "DRAFT", "Draft", "operator", Now);
        var activeCategory = new Category(Guid.NewGuid(), "ACTIVE", "Active", "operator", Now);
        activeCategory.Activate("operator", Now);
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(value => value.FindAsync(draftCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draftCategory);
        categories.Setup(value => value.FindAsync(activeCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(activeCategory);
        var products = new Mock<IProductRepository>();
        products.Setup(value => value.SkuExistsAsync("TAKEN", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new CreateDraftProductHandler(
            categories.Object, products.Object, Mock.Of<ICatalogSnapshotPublisher>(), Mock.Of<ICatalogAuditWriter>(), new TestClock(Now));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.Handle(
            DraftCommand(draftCategory.Id, "NEW-SKU"), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.Handle(
            DraftCommand(activeCategory.Id, " taken "), CancellationToken.None));

        products.Verify(value => value.Add(It.IsAny<Product>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateDraftProduct_ActiveCategory_AddsDraftPublishesSnapshotAndAudits()
    {
        var category = new Category(Guid.NewGuid(), "ACTIVE", "Active", "operator", Now);
        category.Activate("operator", Now);
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(value => value.FindAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        var products = new Mock<IProductRepository>();
        Product? added = null;
        products.Setup(value => value.Add(It.IsAny<Product>())).Callback<Product>(value => added = value);
        var snapshots = new Mock<ICatalogSnapshotPublisher>();
        var audit = new Mock<ICatalogAuditWriter>();
        var handler = new CreateDraftProductHandler(
            categories.Object, products.Object, snapshots.Object, audit.Object, new TestClock(Now));

        var id = await handler.Handle(DraftCommand(category.Id, "sku-1"), CancellationToken.None);

        Assert.IsNotNull(added);
        Assert.AreEqual(id, added.Id);
        Assert.AreEqual(ProductLifecycleStatus.Draft, added.LifecycleStatus);
        Assert.AreEqual("SKU-1", added.NormalizedSku);
        Assert.AreEqual(Now, added.CreatedOnUtc);
        snapshots.Verify(value => value.Publish(added), Times.Once);
        audit.Verify(value => value.Record("Product", id, "DraftCreated", "operator", Now, It.IsAny<object?>()), Times.Once);
    }

    [TestMethod]
    public void SnapshotPublisher_NoOpAggregate_WritesNothingButChangedProductWritesEveryVariant()
    {
        var messages = new List<OutboxMessage>();
        var outbox = new Mock<IOutboxStore>();
        outbox.Setup(value => value.Add(It.IsAny<OutboxMessage>())).Callback<OutboxMessage>(messages.Add);
        var publisher = new OutboxCatalogSnapshotPublisher(outbox.Object);
        var product = CreateProduct();
        product.AddVariant(Guid.NewGuid(), "SKU-RED", "Red", "{}", null, 11m, "CNY", false, "operator", Now.AddMinutes(1));
        product.ClearDomainEvents();

        publisher.Publish(product);
        Assert.IsEmpty(messages);

        product.Publish("operator", Now.AddMinutes(2));
        publisher.Publish(product);

        var events = messages
            .Select(message => JsonSerializer.Deserialize<ProductVariantChangedIntegrationEvent>(message.Payload)!)
            .ToArray();
        Assert.HasCount(2, events);
        Assert.IsTrue(events.All(value => value.Revision == product.Revision));
        Assert.IsTrue(events.Single(value => value.Sku == "SKU-BASE").IsActive);
        Assert.IsFalse(events.Single(value => value.Sku == "SKU-RED").IsActive, "An inactive variant must not become sellable.");
    }

    [TestMethod]
    public void SnapshotPublisher_RemovedVariantTombstone_IsAlwaysWritten()
    {
        var messages = new List<OutboxMessage>();
        var outbox = new Mock<IOutboxStore>();
        outbox.Setup(value => value.Add(It.IsAny<OutboxMessage>())).Callback<OutboxMessage>(messages.Add);
        var product = CreateProduct();
        product.ClearDomainEvents();

        new OutboxCatalogSnapshotPublisher(outbox.Object).PublishUnavailable(product, product.DefaultVariant);

        var tombstone = JsonSerializer.Deserialize<ProductVariantChangedIntegrationEvent>(messages.Single().Payload)!;
        Assert.IsFalse(tombstone.IsActive);
        Assert.AreEqual(product.DefaultVariant.Id, tombstone.VariantId);
    }

    [TestMethod]
    public void PromotionDefinitionMapper_CanonicalizesTargetsAndRejectsUnknownValues()
    {
        var targets = PromotionDefinitionMapper.ToTargets(
        [
            new PromotionTargetInput("sku", "SKU-1", false),
            new PromotionTargetInput("CATEGORY", Guid.Empty.ToString(), true)
        ]).ToArray();

        CollectionAssert.AreEqual(new[] { "Sku", "Category" }, targets.Select(target => target.TargetType).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, targets.Select(target => target.Sequence).ToArray());
        Assert.AreEqual(PromotionStackPolicy.Exclusive,
            PromotionDefinitionMapper.ParseEnum<PromotionStackPolicy>("exclusive", "StackPolicy"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PromotionDefinitionMapper.ToTargets([new PromotionTargetInput("Brand", "x", false)]).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() =>
            PromotionDefinitionMapper.ParseEnum<PromotionTriggerType>("Manual", "TriggerType"));
    }

    [TestMethod]
    public void VariantValidators_RejectNonObjectAttributesAndInvalidCurrency()
    {
        var validator = new CreateProductVariantValidator();

        var result = validator.Validate(new CreateProductVariantCommand(
            Guid.NewGuid(), "SKU-1", "Blue", "[1,2]", null, 10m, "RMB1", true, "operator"));
        var valid = validator.Validate(new CreateProductVariantCommand(
            Guid.NewGuid(), "SKU-1", "Blue", "", null, 10m, "cny", true, "operator"));

        CollectionAssert.AreEquivalent(
            new[] { nameof(CreateProductVariantCommand.AttributesJson), nameof(CreateProductVariantCommand.Currency) },
            result.Errors.Select(error => error.PropertyName).ToArray());
        Assert.IsTrue(valid.IsValid);
    }

    [TestMethod]
    public void Category_ArchivedIsTerminalAndArchiveIsIdempotent()
    {
        var category = new Category(Guid.NewGuid(), " phones ", "Phones", "operator", Now);
        category.Activate("operator", Now.AddMinutes(1));
        category.Archive("operator", Now.AddMinutes(2));
        var version = category.Version;

        category.Archive("operator", Now.AddMinutes(3));

        Assert.AreEqual("PHONES", category.NormalizedCode);
        Assert.AreEqual(version, category.Version);
        Assert.ThrowsExactly<InvalidOperationException>(() => category.Rename("New", "operator", Now.AddMinutes(4)));
        Assert.ThrowsExactly<InvalidOperationException>(() => category.Activate("operator", Now.AddMinutes(4)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new Category(Guid.NewGuid(), "C", "N", "operator", Now).Rename("Earlier", "operator", Now.AddTicks(-1)));
    }

    [TestMethod]
    public void Coupon_BucketedQuotaSplitsRemainderAndHidesPlainCode()
    {
        var coupon = new Coupon(Guid.NewGuid(), Guid.NewGuid(), " save-2026 ", Now, Now.AddDays(1), " buyer-1 ",
            10, 2, CouponQuotaStrategy.Bucketed, 3, "operator", Now);

        CollectionAssert.AreEqual(new[] { 4, 3, 3 }, coupon.QuotaBuckets.Select(bucket => bucket.Capacity).ToArray());
        Assert.AreEqual("****2026", coupon.MaskedCode);
        Assert.AreEqual(Coupon.ComputeCodeDigest("SAVE-2026"), coupon.CodeDigest);
        Assert.IsFalse(coupon.IsAvailableFor("buyer-1", Now.AddHours(1)), "Draft coupons are not redeemable.");

        coupon.Activate("operator", Now.AddMinutes(1));

        Assert.IsTrue(coupon.IsAvailableFor("buyer-1", Now.AddHours(1)));
        Assert.IsFalse(coupon.IsAvailableFor("buyer-2", Now.AddHours(1)));
        Assert.IsFalse(coupon.IsAvailableFor("buyer-1", Now.AddDays(1)), "Validity end is exclusive.");
        Assert.ThrowsExactly<InvalidOperationException>(() => coupon.Expire("operator", Now.AddHours(2)));
    }

    [TestMethod]
    public void Coupon_InvalidQuotaConfiguration_IsRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new Coupon(Guid.NewGuid(), Guid.NewGuid(), "SAVE-2026",
            Now, Now.AddDays(1), null, null, 1, CouponQuotaStrategy.Bucketed, 2, "operator", Now));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Coupon(Guid.NewGuid(), Guid.NewGuid(), "SAVE-2026",
            Now, Now.AddDays(1), null, 5, 6, CouponQuotaStrategy.SingleRow, null, "operator", Now));
        Assert.ThrowsExactly<ArgumentException>(() => new Coupon(Guid.NewGuid(), Guid.NewGuid(), "SHORT",
            Now, Now.AddDays(1), null, null, 1, CouponQuotaStrategy.SingleRow, null, "operator", Now));
    }

    [TestMethod]
    public void Promotion_PublishRequiresBenefitAndFreezesDefinition()
    {
        var promotion = new Promotion(Guid.NewGuid(), "summer", "Summer", PromotionTriggerType.Automatic,
            Now, Now.AddDays(7), 10, PromotionStackPolicy.Stackable, null, "operator", Now);

        Assert.ThrowsExactly<InvalidOperationException>(() => promotion.Publish("operator", Now.AddMinutes(1)));

        promotion.ReplaceDefinition(
            Array.Empty<PromotionRuleDefinition>(),
            [new PromotionBenefitDefinition("FixedAmountOff", 1, "{\"amount\":5}", 0)],
            Array.Empty<PromotionTargetDefinition>(),
            "operator",
            Now.AddMinutes(1));
        promotion.Publish("operator", Now.AddMinutes(2));

        Assert.AreEqual(PromotionStatus.Active, promotion.Status);
        Assert.AreEqual(promotion.Revision, promotion.PublishedRevision);
        Assert.AreEqual(64, promotion.DefinitionChecksum.Length);
        Assert.ThrowsExactly<InvalidOperationException>(() => promotion.ReplaceDefinition(
            Array.Empty<PromotionRuleDefinition>(),
            Array.Empty<PromotionBenefitDefinition>(),
            Array.Empty<PromotionTargetDefinition>(),
            "operator",
            Now.AddMinutes(3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => promotion.Archive("operator", Now.AddMinutes(3)));
    }

    private static CreateDraftProductCommand DraftCommand(Guid categoryId, string sku) =>
        new(categoryId, sku, "Product", "Description", 10m, "CNY", "operator");

    private static Product CreateProduct() => new(
        Guid.NewGuid(), Guid.NewGuid(), "SKU-BASE", "Product", "Description", 10m, "CNY", "operator", Now);
}
