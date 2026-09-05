using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Application.FlashSales;

public sealed record GetFlashSaleQuery(Guid CampaignId) : IQuery<FlashSaleAvailabilityDto?>;

/// <summary>Current durable inventory view for a flash-sale campaign.</summary>
public sealed record FlashSaleAvailabilityDto(
    Guid CampaignId,
    string Sku,
    FlashSaleStockStrategy Strategy,
    string State,
    int TotalQuantity,
    int ReservedQuantity,
    int SoldQuantity,
    int ReleasedQuantity,
    int AvailableQuantity,
    int PurchaseLimit,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc);

internal sealed class GetFlashSaleQueryHandler(
    InventoryDbContext db,
    IDistributedCache cache,
    IOptions<FlashSaleOptions> options,
    TimeProvider clock,
    ILogger<GetFlashSaleQueryHandler> logger) : IRequestHandler<GetFlashSaleQuery, FlashSaleAvailabilityDto?>
{
    public async Task<FlashSaleAvailabilityDto?> Handle(
        GetFlashSaleQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = FlashSaleCacheKeys.Availability(request.CampaignId);
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
                return JsonSerializer.Deserialize<FlashSaleAvailabilityDto>(cached);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Flash-sale availability cache read failed for {CampaignId}; using PostgreSQL.", request.CampaignId);
        }

        var snapshot = await db.FlashSaleCampaigns
            .AsNoTracking()
            .Where(campaign => campaign.Id == request.CampaignId)
            .Select(campaign => new
            {
                campaign.Id,
                campaign.Sku,
                campaign.Strategy,
                campaign.Status,
                campaign.TotalQuantity,
                campaign.PurchaseLimit,
                campaign.StartsOnUtc,
                campaign.EndsOnUtc,
                ReservedQuantity = campaign.StockBuckets.Sum(bucket => bucket.ReservedQuantity),
                SoldQuantity = campaign.StockBuckets.Sum(bucket => bucket.SoldQuantity),
                ReleasedQuantity = campaign.StockBuckets.Sum(bucket => bucket.ReleasedQuantity)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
            return null;

        var availableQuantity = snapshot.TotalQuantity
            - snapshot.ReservedQuantity
            - snapshot.SoldQuantity
            - snapshot.ReleasedQuantity;
        var now = clock.GetUtcNow();
        var state = snapshot.Status == FlashSaleCampaignStatus.Closed
            ? "Closed"
            : now < snapshot.StartsOnUtc
                ? "Scheduled"
                : now >= snapshot.EndsOnUtc
                    ? "Expired"
                    : availableQuantity == 0
                        ? "SoldOut"
                        : "Active";
        var result = new FlashSaleAvailabilityDto(
            snapshot.Id,
            snapshot.Sku,
            snapshot.Strategy,
            state,
            snapshot.TotalQuantity,
            snapshot.ReservedQuantity,
            snapshot.SoldQuantity,
            snapshot.ReleasedQuantity,
            availableQuantity,
            snapshot.PurchaseLimit,
            snapshot.StartsOnUtc,
            snapshot.EndsOnUtc);

        try
        {
            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(result),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = options.Value.AvailabilityCacheDuration
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Flash-sale availability cache write failed for {CampaignId}.", request.CampaignId);
        }

        return result;
    }
}

public sealed record GetExpiredFlashSalesQuery(int BatchSize) : IQuery<IReadOnlyList<Guid>>;

internal sealed class GetExpiredFlashSalesQueryHandler(
    InventoryDbContext db) : IRequestHandler<GetExpiredFlashSalesQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(
        GetExpiredFlashSalesQuery request,
        CancellationToken cancellationToken) =>
        await db.FlashSaleCampaigns
            .FromSqlInterpolated($"SELECT * FROM flash_sale_campaigns WHERE \"Status\" = 0 AND \"EndsOnUtc\" <= CURRENT_TIMESTAMP ORDER BY \"EndsOnUtc\" LIMIT {request.BatchSize}")
            .AsNoTracking()
            .OrderBy(campaign => campaign.EndsOnUtc)
            .Select(campaign => campaign.Id)
            .ToArrayAsync(cancellationToken);
}
