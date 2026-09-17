using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.CatalogService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class CatalogProductTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AddVariant_ValidSku_AdvancesVersionAndRevisionWithoutReplacingDefault()
    {
        var product = CreateProduct();
        var variantId = Guid.NewGuid();

        var variant = product.AddVariant(
            variantId, "  sku-blue  ", "Blue", "{\"color\":\"blue\"}", null,
            12.345m, "cny", true, "operator", Created.AddMinutes(1));

        Assert.AreEqual(variantId, variant.Id);
        Assert.AreEqual("SKU-BLUE", variant.NormalizedSku);
        Assert.AreEqual(12.35m, variant.Price);
        Assert.AreEqual(product.Id, product.DefaultVariant.Id);
        Assert.AreEqual(2, product.Variants.Count);
        Assert.AreEqual(2L, product.Version);
        Assert.AreEqual(2L, product.Revision);
        Assert.AreEqual("VariantAdded", product.DomainEvents.OfType<ProductSnapshotChangedDomainEvent>().Last().Change);
    }

    [TestMethod]
    public void AddVariant_MismatchedCurrency_DoesNotMutateAggregate()
    {
        var product = CreateProduct();

        Assert.ThrowsExactly<InvalidOperationException>(() => product.AddVariant(
            Guid.NewGuid(), "SKU-USD", "Dollar", "{}", null,
            10m, "USD", true, "operator", Created.AddMinutes(1)));

        Assert.AreEqual(1, product.Variants.Count);
        Assert.AreEqual(1L, product.Version);
        Assert.AreEqual(1L, product.Revision);
    }

    [TestMethod]
    public void Publish_FutureEffectiveTime_ControlsSellabilityAndArchiveRequiresUnpublish()
    {
        var product = CreateProduct();
        var effective = Created.AddHours(1);

        product.Publish(effective, "operator", Created.AddMinutes(1));

        Assert.IsFalse(product.IsSellableAt(effective.AddTicks(-1)));
        Assert.IsTrue(product.IsSellableAt(effective));
        Assert.ThrowsExactly<InvalidOperationException>(() => product.Archive("operator", Created.AddHours(2)));
        product.Unpublish("operator", Created.AddHours(2));
        product.Archive("operator", Created.AddHours(3));
        Assert.AreEqual(ProductLifecycleStatus.Archived, product.LifecycleStatus);
        Assert.IsFalse(product.IsSellableAt(Created.AddHours(4)));
        Assert.AreEqual(4L, product.Version);
    }

    [TestMethod]
    public void RemoveVariant_RemovesItsMediaButRejectsDefaultVariant()
    {
        var product = CreateProduct();
        var variant = product.AddVariant(Guid.NewGuid(), "SKU-RED", "Red", "{}", null, 10m,
            "CNY", true, "operator", Created.AddMinutes(1));
        product.AddMedia(Guid.NewGuid(), variant.Id, ProductMediaType.Image, "https://example.test/red.jpg",
            null, null, 0, "operator", Created.AddMinutes(2));

        product.RemoveVariant(variant.Id, "operator", Created.AddMinutes(3));

        Assert.AreEqual(0, product.Media.Count);
        Assert.AreEqual(1, product.Variants.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            product.RemoveVariant(product.DefaultVariant.Id, "operator", Created.AddMinutes(4)));
        Assert.AreEqual(4L, product.Revision);
    }

    [TestMethod]
    public void UpdateDetails_UnchangedValues_DoesNotCreateNewRevision()
    {
        var product = CreateProduct();
        var originalEvents = product.DomainEvents.Count;

        product.UpdateDetails("Product", "Description", "operator", Created.AddMinutes(1));

        Assert.AreEqual(1L, product.Version);
        Assert.AreEqual(1L, product.Revision);
        Assert.AreEqual(originalEvents, product.DomainEvents.Count);
    }

    private static Product CreateProduct() => new(
        Guid.NewGuid(), Guid.NewGuid(), "SKU-BASE", "Product", "Description",
        10m, "CNY", "operator", Created);
}
