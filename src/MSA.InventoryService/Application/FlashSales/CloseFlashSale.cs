using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Application.FlashSales;

public sealed record CloseFlashSaleCommand(Guid CampaignId) : ICommand<CloseFlashSaleResult>;

public sealed record CloseFlashSaleResult(bool Found, bool Changed, int ReleasedQuantity);

internal sealed class CloseFlashSaleCommandHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    IOutboxStore outbox,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IRequestHandler<CloseFlashSaleCommand, CloseFlashSaleResult>
{
    public async Task<CloseFlashSaleResult> Handle(
        CloseFlashSaleCommand request,
        CancellationToken cancellationToken)
    {
        var campaign = await db.FlashSaleCampaigns
            .FromSqlInterpolated($"SELECT * FROM inventory_flash_sale_campaigns WHERE \"Id\" = {request.CampaignId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (campaign is null)
            return new CloseFlashSaleResult(false, false, 0);
        if (campaign.Status == FlashSaleCampaignStatus.Closed)
            return new CloseFlashSaleResult(true, false, 0);

        await db.Entry(campaign).Collection(value => value.StockBuckets).LoadAsync(cancellationToken);
        await transactionLock.AcquireStockItemsAsync([campaign.Sku], cancellationToken);
        var stockItem = await db.StockItems.SingleAsync(item => item.Sku == campaign.Sku, cancellationToken);

        var now = clock.GetUtcNow();
        var releasedQuantity = campaign.Close(now);
        if (releasedQuantity > 0)
        {
            stockItem.Release(releasedQuantity);
            db.StockMovements.Add(StockMovement.Record(
                stockItem,
                StockMovementType.CampaignReleased,
                onHandDelta: 0,
                reservedDelta: -releasedQuantity,
                StockMovementReferenceType.FlashSaleCampaign,
                campaign.Id,
                "Unused flash-sale inventory returned to the regular stock pool.",
                now));
        }

        var integrationEvent = new FlashSaleCampaignClosedIntegrationEvent
        {
            CampaignId = campaign.Id,
            ClosedOnUtc = now
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

        await cache.RemoveStockAsync(campaign.Sku, cancellationToken);
        await cache.RemoveFlashSaleAsync(campaign.Id, cancellationToken);

        return new CloseFlashSaleResult(true, true, releasedQuantity);
    }
}
