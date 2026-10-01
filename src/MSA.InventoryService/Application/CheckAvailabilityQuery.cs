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

internal sealed class CheckAvailabilityQueryHandler(
    InventoryReadModel reads,
    InventoryDbContext db,
    TimeProvider clock)
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

        // 库存余额与配额都走读模型缓存：秒杀开抢时这条预检每秒被调用成千上万次，不能每次都打到数据库。
        // 预检允许毫秒级的旧值，真正的把关在预占事务里。
        var skus = lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToArray();
        var stock = await reads.StocksAsync(skus, cancellationToken);
        var offeredSkus = lines.Where(line => line.OfferId.HasValue).Select(line => line.Sku).Distinct(StringComparer.Ordinal);
        var offers = new List<Allocations.InventoryAllocationDto>();
        foreach (var sku in offeredSkus)
            offers.AddRange(await reads.OffersAsync(sku, cancellationToken));

        var allocationIds = offers.Select(offer => offer.AllocationId).ToArray();
        // 每人限购是按人计的，不进共享缓存，只查这一个人。
        var buyers = offers.Count == 0 || string.IsNullOrWhiteSpace(request.UserId)
            ? []
            : await db.InventoryAllocationBuyers
                .AsNoTracking()
                .Where(buyer => allocationIds.Contains(buyer.AllocationId)
                    && buyer.UserId == request.UserId)
                .ToArrayAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var results = lines.Select(line => Evaluate(line, stock, offers, buyers, now)).ToArray();
        return new AvailabilitySnapshot(results.All(result => result.Sufficient), results);
    }

    private static AvailabilityLineResult Evaluate(
        AvailabilityRequestLine line,
        IReadOnlyDictionary<string, InventoryAvailabilityDto> stock,
        IReadOnlyList<Allocations.InventoryAllocationDto> offers,
        IReadOnlyList<InventoryAllocationBuyer> buyers,
        DateTimeOffset now)
    {
        if (line.OfferId is Guid offerId)
        {
            var offer = offers.SingleOrDefault(value => value.OfferId == offerId && value.Sku == line.Sku);
            // 读模型只保留未关闭的配额：不在其中就是已关闭（或从未存在，订单侧的投影预检会先挡掉那种）。
            if (offer is null)
                return new AvailabilityLineResult(line.Sku, 0, false, "OfferClosed");
            if (offer.State == "Closed" || now < offer.StartsOnUtc || now >= offer.EndsOnUtc)
                return new AvailabilityLineResult(line.Sku, offer.AvailableQuantity, false, "OfferClosed");

            var buyer = buyers.SingleOrDefault(value => value.AllocationId == offer.AllocationId);
            var used = buyer is null ? 0 : buyer.ReservedQuantity + buyer.SoldQuantity;
            if ((long)used + line.Quantity > offer.PerUserLimit)
                return new AvailabilityLineResult(line.Sku, offer.AvailableQuantity, false, "OfferLimitExceeded");

            return offer.AvailableQuantity >= line.Quantity
                ? new AvailabilityLineResult(line.Sku, offer.AvailableQuantity, true, null)
                : new AvailabilityLineResult(line.Sku, offer.AvailableQuantity, false, "OfferSoldOut");
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
