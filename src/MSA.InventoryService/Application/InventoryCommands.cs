using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSA.Infrastrucute.Mediator;
using MSA.Persistence.Postgres;
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

        return item.Id;
    }
}

public sealed record GetInventoryAvailabilityQuery(string Sku) : IQuery<InventoryAvailabilityDto?>;

/// <summary>Current durable inventory view for a SKU.</summary>
public sealed record InventoryAvailabilityDto(string Sku, int OnHand, int Reserved, int Available, long Version);

internal sealed class GetInventoryAvailabilityHandler(InventoryReadModel reads)
    : IRequestHandler<GetInventoryAvailabilityQuery, InventoryAvailabilityDto?>
{
    public Task<InventoryAvailabilityDto?> Handle(
        GetInventoryAvailabilityQuery request,
        CancellationToken cancellationToken) =>
        reads.StockAsync(StockItem.NormalizeSku(request.Sku), cancellationToken);
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
            // SKU 存的是大写标准形式，用 LIKE（区分大小写）就够了；写成 Contains 会被翻译成 strpos，用不上三元组索引。
            var pattern = PostgresLike.Contains(request.Search.ToUpperInvariant());
            query = query.Where(item => EF.Functions.Like(item.Sku, pattern, PostgresLike.Escape));
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
