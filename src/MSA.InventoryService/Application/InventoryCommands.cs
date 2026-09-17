using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Application;

public sealed record AddStockCommand(string Sku, int Quantity) : ICommand<Guid>;

public sealed class AddStockValidator : AbstractValidator<AddStockCommand>
{
    public AddStockValidator()
    {
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

internal sealed class AddStockHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    InventoryCacheInvalidator cache,
    TimeProvider clock) : IRequestHandler<AddStockCommand, Guid>
{
    public async Task<Guid> Handle(AddStockCommand request, CancellationToken cancellationToken)
    {
        var sku = StockItem.NormalizeSku(request.Sku);
        await transactionLock.AcquireStockItemsAsync([sku], cancellationToken);

        var item = await db.StockItems.SingleOrDefaultAsync(value => value.Sku == sku, cancellationToken);
        if (item is null)
        {
            item = new StockItem(Guid.NewGuid(), sku, request.Quantity);
            db.Add(item);
        }
        else
        {
            item.Increase(request.Quantity);
        }

        db.StockMovements.Add(StockMovement.Record(
            item,
            StockMovementType.StockAdded,
            request.Quantity,
            reservedDelta: 0,
            StockMovementReferenceType.None,
            referenceId: null,
            "Stock added through the compatibility endpoint.",
            clock.GetUtcNow()));

        cache.StockChanged(sku);
        return item.Id;
    }
}

public sealed record GetInventoryAvailabilityQuery(string Sku) : IQuery<InventoryAvailabilityDto?>;

/// <summary>Current durable inventory view for a SKU.</summary>
public sealed record InventoryAvailabilityDto(string Sku, int OnHand, int Reserved, int Available, long Version);

internal sealed class GetInventoryAvailabilityHandler(
    InventoryDbContext db,
    IDistributedCache cache,
    ILogger<GetInventoryAvailabilityHandler> logger) : IRequestHandler<GetInventoryAvailabilityQuery, InventoryAvailabilityDto?>
{
    public async Task<InventoryAvailabilityDto?> Handle(
        GetInventoryAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        var sku = StockItem.NormalizeSku(request.Sku);
        var key = InventoryCacheKeys.Stock(sku);
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is not null)
                return JsonSerializer.Deserialize<InventoryAvailabilityDto>(cached);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Inventory cache read failed for {Sku}; using PostgreSQL.", sku);
        }

        var value = await db.StockItems
            .AsNoTracking()
            .Where(item => item.Sku == sku)
            .Select(item => new InventoryAvailabilityDto(
                item.Sku,
                item.OnHand,
                item.Reserved,
                item.OnHand - item.Reserved,
                item.Version))
            .SingleOrDefaultAsync(cancellationToken);
        if (value is null)
            return null;

        try
        {
            await cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15) },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Inventory cache write failed for {Sku}.", sku);
        }

        return value;
    }
}

public sealed record ListInventoryStockQuery(
    string? Search,
    int? LowStockThreshold,
    int Skip,
    int Take) : IQuery<InventoryStockPage>;

public sealed record InventoryStockItemDto(
    Guid Id,
    string Sku,
    int OnHand,
    int Reserved,
    int Available,
    long Version);

public sealed record InventoryStockPage(
    IReadOnlyList<InventoryStockItemDto> Items,
    int Skip,
    int Take,
    int Total);

internal sealed class ListInventoryStockHandler(InventoryDbContext db)
    : IRequestHandler<ListInventoryStockQuery, InventoryStockPage>
{
    public async Task<InventoryStockPage> Handle(
        ListInventoryStockQuery request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 200);
        var query = db.StockItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(item => item.Sku.Contains(search));
        }
        if (request.LowStockThreshold is { } threshold)
            query = query.Where(item => item.OnHand - item.Reserved <= threshold);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.Sku)
            .Skip(skip)
            .Take(take)
            .Select(item => new InventoryStockItemDto(
                item.Id,
                item.Sku,
                item.OnHand,
                item.Reserved,
                item.OnHand - item.Reserved,
                item.Version))
            .ToArrayAsync(cancellationToken);
        return new InventoryStockPage(items, skip, take, total);
    }
}
