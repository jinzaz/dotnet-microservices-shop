using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrdersSummary;

public sealed record OrderStatusCountDto(string Status, int Count);

public sealed record OrderMoneyTotalDto(string Currency, decimal Amount);

public sealed record OrderDailyTrendDto(
    DateOnly Date,
    int OrderCount,
    int PaidOrderCount,
    IReadOnlyList<OrderMoneyTotalDto> PaidAmounts);

public sealed record OrdersSummaryDto(
    int TotalOrders,
    IReadOnlyList<OrderStatusCountDto> OrderStatuses,
    IReadOnlyList<OrderStatusCountDto> FulfillmentStatuses,
    IReadOnlyList<OrderDailyTrendDto> Trend,
    DateTimeOffset GeneratedOnUtc);

/// <param name="Days">Trailing days, including today, in the operator's time zone.</param>
/// <param name="UtcOffsetMinutes">Operator offset from UTC, e.g. 480 for UTC+8.</param>
public sealed record GetOrdersSummaryQuery(int Days, int UtcOffsetMinutes) : IQuery<OrdersSummaryDto>;

internal sealed class GetOrdersSummaryQueryHandler(OrderDbContext db, TimeProvider clock)
    : IRequestHandler<GetOrdersSummaryQuery, OrdersSummaryDto>
{
    public async Task<OrdersSummaryDto> Handle(GetOrdersSummaryQuery request, CancellationToken cancellationToken)
    {
        if (request.Days is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(request), "Days must be between 1 and 31.");
        if (request.UtcOffsetMinutes is < -14 * 60 or > 14 * 60)
            throw new ArgumentOutOfRangeException(nameof(request), "UtcOffsetMinutes must be within ±14 hours.");

        var now = clock.GetUtcNow();
        var offset = TimeSpan.FromMinutes(request.UtcOffsetMinutes);
        var today = DateOnly.FromDateTime(now.ToOffset(offset).DateTime);
        var firstDay = today.AddDays(1 - request.Days);
        var fromUtc = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime();

        var orderStatuses = await db.Orders.AsNoTracking()
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var fulfillmentStatuses = await db.Orders.AsNoTracking()
            .GroupBy(order => order.FulfillmentStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // ponytail: window rows are bucketed in memory to respect the operator time zone; move to a
        // SQL date_trunc projection if a 31-day window ever exceeds a few hundred thousand orders.
        var created = await db.Orders.AsNoTracking()
            .Where(order => order.CreatedOnUtc >= fromUtc)
            .Select(order => order.CreatedOnUtc)
            .ToListAsync(cancellationToken);
        var paid = await db.Orders.AsNoTracking()
            .Where(order => order.ConfirmedOnUtc >= fromUtc)
            .Select(order => new { ConfirmedOnUtc = order.ConfirmedOnUtc!.Value, order.Amount, order.Currency })
            .ToListAsync(cancellationToken);

        DateOnly LocalDay(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(offset).DateTime);
        var createdByDay = created.GroupBy(LocalDay).ToDictionary(group => group.Key, group => group.Count());
        var paidByDay = paid.GroupBy(item => LocalDay(item.ConfirmedOnUtc)).ToDictionary(group => group.Key, group => group.ToArray());

        var trend = Enumerable.Range(0, request.Days)
            .Select(index => firstDay.AddDays(index))
            .Select(day =>
            {
                var paidItems = paidByDay.GetValueOrDefault(day) ?? [];
                return new OrderDailyTrendDto(
                    day,
                    createdByDay.GetValueOrDefault(day),
                    paidItems.Length,
                    paidItems
                        .GroupBy(item => item.Currency, StringComparer.Ordinal)
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => new OrderMoneyTotalDto(group.Key, group.Sum(item => item.Amount)))
                        .ToArray());
            })
            .ToArray();

        return new OrdersSummaryDto(
            orderStatuses.Sum(item => item.Count),
            Enum.GetValues<OrderStatus>()
                .Select(status => new OrderStatusCountDto(
                    status.ToString(),
                    orderStatuses.SingleOrDefault(item => item.Status == status)?.Count ?? 0))
                .ToArray(),
            Enum.GetValues<OrderFulfillmentStatus>()
                .Select(status => new OrderStatusCountDto(
                    status.ToString(),
                    fulfillmentStatuses.SingleOrDefault(item => item.Status == status)?.Count ?? 0))
                .ToArray(),
            trend,
            now);
    }
}
