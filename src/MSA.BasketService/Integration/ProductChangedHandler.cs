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
            value => value.ProductId == integrationEvent.ProductId,
            cancellationToken);
        if (snapshot is null)
        {
            Db.ProductSnapshots.Add(new BasketProductSnapshot(
                integrationEvent.ProductId,
                integrationEvent.Sku,
                integrationEvent.Name,
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
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.Revision,
                effectiveOnUtc);
        }
    }
}
