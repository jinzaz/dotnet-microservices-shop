using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Application.FlashSales;
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
    InventoryCacheInvalidator cache) : IRequestHandler<AddStockCommand, Guid>
{
    public async Task<Guid> Handle(AddStockCommand request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim();
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

        await cache.RemoveStockAsync(sku, cancellationToken);
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
        var sku = request.Sku.Trim();
        var key = $"inventory:availability:{sku}";
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
