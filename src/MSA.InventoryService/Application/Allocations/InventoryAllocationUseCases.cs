using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;
using System.Text.Json;

namespace MSA.InventoryService.Application.Allocations;

public sealed record CreateInventoryAllocationCommand(
    Guid OfferId,
    string Sku,
    int TotalQuantity,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int PerUserLimit) : ICommand<Guid>;

public sealed class CreateInventoryAllocationValidator : AbstractValidator<CreateInventoryAllocationCommand>
{
    public CreateInventoryAllocationValidator()
    {
        RuleFor(command => command.OfferId).NotEmpty();
        RuleFor(command => command.Sku).NotEmpty().MaximumLength(80);
        RuleFor(command => command.TotalQuantity).InclusiveBetween(1, 10_000_000);
        RuleFor(command => command.PerUserLimit).GreaterThan(0);
        RuleFor(command => command)
            .Must(command => command.PerUserLimit <= command.TotalQuantity)
            .WithMessage("PerUserLimit cannot exceed TotalQuantity.");
        RuleFor(command => command)
            .Must(command => command.StartsOnUtc < command.EndsOnUtc)
            .WithMessage("EndsOnUtc must be after StartsOnUtc.");
    }
}

internal sealed class CreateInventoryAllocationHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    IOutboxStore outbox,
    TimeProvider clock) : IRequestHandler<CreateInventoryAllocationCommand, Guid>
{
    public async Task<Guid> Handle(
        CreateInventoryAllocationCommand request,
        CancellationToken cancellationToken)
    {
        var sku = StockItem.NormalizeSku(request.Sku);
        await transactionLock.AcquireStockItemsAsync([sku], cancellationToken);
        if (await db.InventoryAllocations.AnyAsync(
                value => value.OfferId == request.OfferId && value.Sku == sku,
                cancellationToken))
        {
            throw new InvalidOperationException("This offer already has an inventory allocation for the SKU.");
        }

        var stockItem = await db.StockItems.SingleOrDefaultAsync(item => item.Sku == sku, cancellationToken)
            ?? throw new KeyNotFoundException($"Inventory item '{sku}' was not found.");
        stockItem.Reserve(request.TotalQuantity);

        var now = clock.GetUtcNow();
        var allocation = new InventoryAllocation(
            Guid.NewGuid(),
            request.OfferId,
            sku,
            request.TotalQuantity,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.PerUserLimit,
            now);
        db.InventoryAllocations.Add(allocation);
        db.StockMovements.Add(StockMovement.Record(
            stockItem,
            StockMovementType.AllocationCreated,
            0,
            request.TotalQuantity,
            StockMovementReferenceType.InventoryAllocation,
            allocation.Id,
            "Inventory ring-fenced for a limited offer.",
            now));

        outbox.AddIntegrationEvent(new InventoryAllocationOpenedIntegrationEvent
        {
            AllocationId = allocation.Id,
            OfferId = allocation.OfferId,
            Sku = allocation.Sku,
            StartsOnUtc = allocation.StartsOnUtc,
            EndsOnUtc = allocation.EndsOnUtc,
            PerUserLimit = allocation.PerUserLimit
        });
        return allocation.Id;
    }
}

public sealed record CloseInventoryAllocationCommand(Guid AllocationId)
    : ICommand<CloseInventoryAllocationResult>;

public sealed record CloseInventoryAllocationResult(bool Found, bool Changed, int ReleasedQuantity);

internal sealed class CloseInventoryAllocationHandler(
    InventoryDbContext db,
    IInventoryTransactionLock transactionLock,
    IOutboxStore outbox,
    TimeProvider clock) : IRequestHandler<CloseInventoryAllocationCommand, CloseInventoryAllocationResult>
{
    public async Task<CloseInventoryAllocationResult> Handle(
        CloseInventoryAllocationCommand request,
        CancellationToken cancellationToken)
    {
        var sku = await db.InventoryAllocations
            .AsNoTracking()
            .Where(value => value.Id == request.AllocationId)
            .Select(value => value.Sku)
            .SingleOrDefaultAsync(cancellationToken);
        if (sku is null)
            return new CloseInventoryAllocationResult(false, false, 0);
        await transactionLock.AcquireStockItemsAsync([sku], cancellationToken);
        var allocation = await InventoryRowLocks.LockAllocationAsync(db, request.AllocationId, cancellationToken)
            ?? throw new InvalidOperationException("Inventory allocation disappeared while being closed.");
        if (allocation.Status == InventoryAllocationStatus.Closed)
            return new CloseInventoryAllocationResult(true, false, 0);

        var stockItem = await db.StockItems.SingleAsync(item => item.Sku == allocation.Sku, cancellationToken);
        var now = clock.GetUtcNow();
        var released = allocation.Close(now);
        if (released > 0)
        {
            stockItem.Release(released);
            db.StockMovements.Add(StockMovement.Record(
                stockItem,
                StockMovementType.AllocationReleased,
                0,
                -released,
                StockMovementReferenceType.InventoryAllocation,
                allocation.Id,
                "Unused offer allocation returned to regular stock.",
                now));
        }

        outbox.AddIntegrationEvent(new InventoryAllocationClosedIntegrationEvent
        {
            AllocationId = allocation.Id,
            OfferId = allocation.OfferId,
            Sku = allocation.Sku,
            ClosedOnUtc = now
        });
        return new CloseInventoryAllocationResult(true, true, released);
    }
}

public sealed record GetInventoryAllocationQuery(Guid AllocationId)
    : IQuery<InventoryAllocationDto?>;

public sealed record ListInventoryAllocationsQuery(
    string? Sku,
    InventoryAllocationStatus? Status,
    int Skip,
    int Take) : IQuery<InventoryAllocationPage>;

public sealed record InventoryAllocationDto(
    Guid AllocationId,
    Guid OfferId,
    string Sku,
    string State,
    int TotalQuantity,
    int ReservedQuantity,
    int SoldQuantity,
    int ReleasedQuantity,
    int AvailableQuantity,
    int PerUserLimit,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc);

public sealed record InventoryAllocationPage(
    IReadOnlyList<InventoryAllocationDto> Items,
    int Skip,
    int Take,
    int Total);

internal sealed class GetInventoryAllocationQueryHandler(
    InventoryDbContext db,
    ICacheStore cache,
    IOptions<InventoryAllocationOptions> options,
    TimeProvider clock)
    : IRequestHandler<GetInventoryAllocationQuery, InventoryAllocationDto?>
{
    public async Task<InventoryAllocationDto?> Handle(
        GetInventoryAllocationQuery request,
        CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrLoadAsync(
            InventoryCacheKeys.Allocation(request.AllocationId),
            async token =>
            {
                var allocation = await db.InventoryAllocations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == request.AllocationId, token);
                return allocation is null ? null : ToDto(allocation, clock.GetUtcNow());
            },
            new CachePolicy(options.Value.AvailabilityCacheDuration),
            cancellationToken);
        return cached is null ? null : AtTime(cached, clock.GetUtcNow());
    }

    /// <summary>缓存里的状态是写入那一刻算的；开始、结束这类随时间变化的状态按读取时刻重算。</summary>
    internal static InventoryAllocationDto AtTime(InventoryAllocationDto value, DateTimeOffset now) =>
        value with
        {
            State = value.State == "Closed" ? "Closed"
                : now < value.StartsOnUtc ? "Scheduled"
                : now >= value.EndsOnUtc ? "Expired"
                : value.AvailableQuantity == 0 ? "SoldOut"
                : "Active"
        };

    internal static InventoryAllocationDto ToDto(InventoryAllocation value, DateTimeOffset now) =>
        new(
            value.Id,
            value.OfferId,
            value.Sku,
            ResolveState(value, now),
            value.TotalQuantity,
            value.ReservedQuantity,
            value.SoldQuantity,
            value.ReleasedQuantity,
            value.AvailableQuantity,
            value.PerUserLimit,
            value.StartsOnUtc,
            value.EndsOnUtc);

    private static string ResolveState(InventoryAllocation value, DateTimeOffset now) =>
        value.Status == InventoryAllocationStatus.Closed ? "Closed"
        : now < value.StartsOnUtc ? "Scheduled"
        : now >= value.EndsOnUtc ? "Expired"
        : value.AvailableQuantity == 0 ? "SoldOut"
        : "Active";
}

internal sealed class ListInventoryAllocationsQueryHandler(
    InventoryDbContext db,
    TimeProvider clock) : IRequestHandler<ListInventoryAllocationsQuery, InventoryAllocationPage>
{
    public async Task<InventoryAllocationPage> Handle(
        ListInventoryAllocationsQuery request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 200);
        var query = db.InventoryAllocations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Sku))
        {
            var sku = StockItem.NormalizeSku(request.Sku);
            query = query.Where(value => value.Sku.Contains(sku));
        }
        if (request.Status.HasValue)
            query = query.Where(value => value.Status == request.Status.Value);
        var total = await query.CountAsync(cancellationToken);
        var values = await query
            .OrderByDescending(value => value.CreatedOnUtc)
            .ThenBy(value => value.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return new InventoryAllocationPage(
            values.Select(value => GetInventoryAllocationQueryHandler.ToDto(value, now)).ToArray(),
            skip,
            take,
            total);
    }
}

public sealed record GetExpiredInventoryAllocationsQuery(int BatchSize)
    : IQuery<IReadOnlyList<Guid>>;

internal sealed class GetExpiredInventoryAllocationsQueryHandler(InventoryDbContext db)
    : IRequestHandler<GetExpiredInventoryAllocationsQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(
        GetExpiredInventoryAllocationsQuery request,
        CancellationToken cancellationToken) =>
        // DateTimeOffset.UtcNow 由 Npgsql 翻译成数据库端的 now()，判定仍然用数据库时间。
        await db.InventoryAllocations
            .AsNoTracking()
            .Where(value => value.Status == InventoryAllocationStatus.Open && value.EndsOnUtc <= DateTimeOffset.UtcNow)
            .OrderBy(value => value.EndsOnUtc)
            .Take(request.BatchSize)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
}

public sealed class InventoryAllocationOptions
{
    public const string SectionName = "InventoryAllocation";
    public TimeSpan AvailabilityCacheDuration { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ExpiryScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int ExpiryBatchSize { get; set; } = 100;
}

/// <summary>某个 SKU 当前的限量配额（秒杀）快照：商品页角标与余量推流都读它。</summary>
public sealed record GetSkuOffersQuery(string Sku) : IQuery<IReadOnlyList<InventoryAllocationDto>>;

internal sealed class GetSkuOffersQueryHandler(InventoryReadModel reads)
    : IRequestHandler<GetSkuOffersQuery, IReadOnlyList<InventoryAllocationDto>>
{
    public Task<IReadOnlyList<InventoryAllocationDto>> Handle(GetSkuOffersQuery request, CancellationToken cancellationToken) =>
        reads.OffersAsync(StockItem.NormalizeSku(request.Sku), cancellationToken);
}
