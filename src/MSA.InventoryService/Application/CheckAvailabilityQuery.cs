using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;

namespace MSA.InventoryService.Application;

public sealed record AvailabilityRequestLine(string Sku, int Quantity, Guid? OfferId);

public sealed record AvailabilityLineResult(string Sku, int Available, bool Sufficient, string? Reason);

public sealed record AvailabilitySnapshot(bool AllAvailable, IReadOnlyList<AvailabilityLineResult> Lines);

/// <summary>
/// 只读的实时可售判断，镜像预占时的那套规则但不加锁、不占用库存。
/// 下单前先问一次，可以在建单之前就拒掉注定失败的请求，省掉「先建单、再异步取消」的往返。
/// 它不是占用凭证：真正的扣减仍然由 Saga 里的预占完成。
/// </summary>
public sealed record CheckAvailabilityQuery(IReadOnlyList<AvailabilityRequestLine> Lines, string? UserId)
    : IQuery<AvailabilitySnapshot>;

internal sealed class CheckAvailabilityQueryHandler(InventoryDbContext db, TimeProvider clock)
    : IRequestHandler<CheckAvailabilityQuery, AvailabilitySnapshot>
{
    public const int MaximumLines = 100;

    public async Task<AvailabilitySnapshot> Handle(CheckAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var lines = (request.Lines ?? [])
            .Where(line => !string.IsNullOrWhiteSpace(line.Sku) && line.Quantity > 0)
            .Select(line => line with { Sku = StockItem.NormalizeSku(line.Sku) })
            .ToArray();
        if (lines.Length == 0)
            return new AvailabilitySnapshot(true, []);
        if (lines.Length > MaximumLines)
            throw new ArgumentException($"At most {MaximumLines} lines can be checked in one call.", nameof(request));

        var now = clock.GetUtcNow();
        var skus = lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        var stock = await db.StockItems
            .AsNoTracking()
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);

        var offerIds = lines.Where(line => line.OfferId.HasValue).Select(line => line.OfferId!.Value).Distinct().ToArray();
        var allocations = offerIds.Length == 0
            ? []
            : await db.InventoryAllocations
                .AsNoTracking()
                .Where(allocation => offerIds.Contains(allocation.OfferId) && skus.Contains(allocation.Sku))
                .ToArrayAsync(cancellationToken);
        var buyers = offerIds.Length == 0 || string.IsNullOrWhiteSpace(request.UserId)
            ? []
            : await db.InventoryAllocationBuyers
                .AsNoTracking()
                .Where(buyer => allocations.Select(allocation => allocation.Id).Contains(buyer.AllocationId)
                    && buyer.UserId == request.UserId)
                .ToArrayAsync(cancellationToken);

        var results = lines.Select(line => Evaluate(line, stock, allocations, buyers, now)).ToArray();
        return new AvailabilitySnapshot(results.All(result => result.Sufficient), results);
    }

    private static AvailabilityLineResult Evaluate(
        AvailabilityRequestLine line,
        IReadOnlyDictionary<string, StockItem> stock,
        IReadOnlyList<InventoryAllocation> allocations,
        IReadOnlyList<InventoryAllocationBuyer> buyers,
        DateTimeOffset now)
    {
        if (line.OfferId is Guid offerId)
        {
            var allocation = allocations.SingleOrDefault(value => value.OfferId == offerId && value.Sku == line.Sku);
            if (allocation is null)
                return new AvailabilityLineResult(line.Sku, 0, false, "OfferNotFound");
            if (!allocation.AcceptsOrders(now))
                return new AvailabilityLineResult(line.Sku, allocation.AvailableQuantity, false, "OfferClosed");

            var buyer = buyers.SingleOrDefault(value => value.AllocationId == allocation.Id);
            var used = buyer is null ? 0 : buyer.ReservedQuantity + buyer.SoldQuantity;
            if ((long)used + line.Quantity > allocation.PerUserLimit)
                return new AvailabilityLineResult(line.Sku, allocation.AvailableQuantity, false, "OfferLimitExceeded");

            return allocation.AvailableQuantity >= line.Quantity
                ? new AvailabilityLineResult(line.Sku, allocation.AvailableQuantity, true, null)
                : new AvailabilityLineResult(line.Sku, allocation.AvailableQuantity, false, "OfferSoldOut");
        }

        if (!stock.TryGetValue(line.Sku, out var item))
            return new AvailabilityLineResult(line.Sku, 0, false, "SkuNotStocked");
        return item.Available >= line.Quantity
            ? new AvailabilityLineResult(line.Sku, item.Available, true, null)
            : new AvailabilityLineResult(line.Sku, item.Available, false, "InsufficientStock");
    }
}

public sealed record ReservationLineDto(string Sku, int Quantity, Guid? OfferId);

public sealed record OrderReservationDto(
    Guid ReservationId,
    string Status,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset ExpiresOnUtc,
    IReadOnlyList<ReservationLineDto> Lines);

/// <summary>某个订单的预占现状。后台详情页用它回答「库存到底占上没有」，不需要翻库存自己的列表页。</summary>
public sealed record GetOrderReservationQuery(Guid OrderId) : IQuery<OrderReservationDto?>;

internal sealed class GetOrderReservationQueryHandler(InventoryDbContext db)
    : IRequestHandler<GetOrderReservationQuery, OrderReservationDto?>
{
    public Task<OrderReservationDto?> Handle(GetOrderReservationQuery request, CancellationToken cancellationToken) =>
        db.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.OrderId == request.OrderId)
            .OrderByDescending(reservation => reservation.CreatedOnUtc)
            .Select(reservation => new OrderReservationDto(
                reservation.Id,
                reservation.Status.ToString(),
                reservation.CreatedOnUtc,
                reservation.ExpiresOnUtc,
                reservation.Lines
                    .Select(line => new ReservationLineDto(line.Sku, line.Quantity, line.OfferId))
                    .ToArray()))
            .FirstOrDefaultAsync(cancellationToken);
}
