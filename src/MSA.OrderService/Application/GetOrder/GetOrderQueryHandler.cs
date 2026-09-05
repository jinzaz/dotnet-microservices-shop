using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrder;

internal sealed class GetOrderQueryHandler(OrderDbContext db) : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(order =>
                order.Id == request.OrderId
                && (request.CanReadAll || order.UserId == request.UserId))
            .Select(order => new
            {
                order.Id,
                order.UserId,
                order.FlashSaleCampaignId,
                order.Amount,
                order.Currency,
                order.Status,
                order.ReservationId,
                order.PaymentId,
                order.CancellationReason,
                Lines = order.Lines
                    .Select(line => new OrderLineDto(
                        line.Sku,
                        line.ProductName,
                        line.Quantity,
                        line.UnitPrice))
                    .ToArray(),
                order.CreatedOnUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
        return order is null
            ? null
            : new OrderDto(
                order.Id,
                order.UserId,
                order.FlashSaleCampaignId,
                order.Amount,
                order.Currency,
                order.Status.ToString(),
                order.ReservationId,
                order.PaymentId,
                order.CancellationReason,
                order.Lines,
                order.CreatedOnUtc);
    }
}
