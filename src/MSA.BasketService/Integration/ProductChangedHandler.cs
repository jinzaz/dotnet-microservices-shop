using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Domain;
using MSA.BasketService.Infrastructure;
using MSA.Contracts;

namespace MSA.BasketService.Integration;

/// <summary>Legacy single-SKU catalog event; the product id doubles as its default variant id.</summary>
internal sealed class ProductChangedHandler(BasketDbContext db)
    : BasketIntegrationEventHandler<ProductChangedIntegrationEvent>(
        db,
        "basket.catalog-product.v1")
{
    protected override Task HandleCoreAsync(
        ProductChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        ProductSnapshotProjection.UpsertAsync(
            Db,
            new ProductSnapshotChange(
                integrationEvent.ProductId,
                integrationEvent.ProductId,
                integrationEvent.Sku,
                integrationEvent.Name,
                integrationEvent.Name,
                "{}",
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.Revision,
                ProductSnapshotProjection.EffectiveOn(integrationEvent.EffectiveOnUtc, integrationEvent.CreationDate)),
            cancellationToken);
}

internal sealed class ProductVariantChangedHandler(BasketDbContext db)
    : BasketIntegrationEventHandler<ProductVariantChangedIntegrationEvent>(
        db,
        "basket.catalog-product-variant.v1")
{
    protected override Task HandleCoreAsync(
        ProductVariantChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        ProductSnapshotProjection.UpsertAsync(
            Db,
            new ProductSnapshotChange(
                integrationEvent.ProductId,
                integrationEvent.VariantId,
                integrationEvent.Sku,
                integrationEvent.ProductName,
                integrationEvent.VariantName,
                integrationEvent.AttributesJson,
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.Revision,
                ProductSnapshotProjection.EffectiveOn(integrationEvent.EffectiveOnUtc, integrationEvent.CreationDate)),
            cancellationToken);
}

internal sealed record ProductSnapshotChange(
    Guid ProductId,
    Guid VariantId,
    string Sku,
    string ProductName,
    string VariantName,
    string AttributesJson,
    decimal Price,
    string Currency,
    bool IsAvailable,
    long Revision,
    DateTimeOffset EffectiveOnUtc);

internal static class ProductSnapshotProjection
{
    public static DateTimeOffset EffectiveOn(DateTimeOffset? effectiveOnUtc, DateTime creationDate) =>
        effectiveOnUtc ?? new DateTimeOffset(DateTime.SpecifyKind(creationDate, DateTimeKind.Utc));

    /// <summary>Creates or advances the local catalog projection; stale revisions are ignored by the snapshot itself.</summary>
    public static async Task UpsertAsync(
        BasketDbContext db,
        ProductSnapshotChange change,
        CancellationToken cancellationToken)
    {
        var snapshot = await db.ProductSnapshots.SingleOrDefaultAsync(
            value => value.VariantId == change.VariantId,
            cancellationToken);
        if (snapshot is null)
        {
            db.ProductSnapshots.Add(new BasketProductSnapshot(
                change.ProductId,
                change.VariantId,
                change.Sku,
                change.ProductName,
                change.VariantName,
                change.AttributesJson,
                change.Price,
                change.Currency,
                change.IsAvailable,
                change.Revision,
                change.EffectiveOnUtc));
            return;
        }

        snapshot.Apply(
            change.Sku,
            change.ProductName,
            change.VariantName,
            change.AttributesJson,
            change.Price,
            change.Currency,
            change.IsAvailable,
            change.Revision,
            change.EffectiveOnUtc);
    }
}
