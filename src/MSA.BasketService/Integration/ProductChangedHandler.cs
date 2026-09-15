using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Domain;
using MSA.BasketService.Infrastructure;
using MSA.Contracts;

namespace MSA.BasketService.Integration;

internal sealed class ProductChangedHandler(BasketDbContext db)
    : BasketIntegrationEventHandler<ProductChangedIntegrationEvent>(
        db,
        "basket.catalog-product.v1")
{
    protected override async Task HandleCoreAsync(
        ProductChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var effectiveOnUtc = integrationEvent.EffectiveOnUtc
            ?? new DateTimeOffset(
                DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        var snapshot = await Db.ProductSnapshots.SingleOrDefaultAsync(
            value => value.VariantId == integrationEvent.ProductId,
            cancellationToken);
        if (snapshot is null)
        {
            Db.ProductSnapshots.Add(new BasketProductSnapshot(
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
                effectiveOnUtc));
        }
        else
        {
            snapshot.Apply(
                integrationEvent.Sku,
                integrationEvent.Name,
                integrationEvent.Name,
                "{}",
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.Revision,
                effectiveOnUtc);
        }
    }
}

internal sealed class ProductVariantChangedHandler(BasketDbContext db)
    : BasketIntegrationEventHandler<ProductVariantChangedIntegrationEvent>(
        db,
        "basket.catalog-product-variant.v1")
{
    protected override async Task HandleCoreAsync(
        ProductVariantChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var effectiveOnUtc = integrationEvent.EffectiveOnUtc
            ?? new DateTimeOffset(
                DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        var snapshot = await Db.ProductSnapshots.SingleOrDefaultAsync(
            value => value.VariantId == integrationEvent.VariantId,
            cancellationToken);
        if (snapshot is null)
        {
            Db.ProductSnapshots.Add(new BasketProductSnapshot(
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
                effectiveOnUtc));
            return;
        }

        snapshot.Apply(
            integrationEvent.Sku,
            integrationEvent.ProductName,
            integrationEvent.VariantName,
            integrationEvent.AttributesJson,
            integrationEvent.Price,
            integrationEvent.Currency,
            integrationEvent.IsActive,
            integrationEvent.Revision,
            effectiveOnUtc);
    }
}
