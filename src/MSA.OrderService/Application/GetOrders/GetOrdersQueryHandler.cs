using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrders;

internal sealed class GetOrdersQueryHandler(OrderDbContext db)
    : IRequestHandler<GetOrdersQuery, IReadOnlyList<OrderListItemDto>>
{
    public async Task<IReadOnlyList<OrderListItemDto>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Skip < 0 || request.Take is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(request), "Skip must be non-negative and take must be between 1 and 200.");

        var query = db.Orders.AsNoTracking();
        if (!request.CanReadAll)
        {
            query = query.Where(order => order.UserId == request.RequestingUserId);
        }
        else if (!string.IsNullOrWhiteSpace(request.UserIdFilter))
        {
            var userId = request.UserIdFilter.Trim();
            query = query.Where(order => order.UserId == userId);
        }

        if (request.FulfillmentStatus.HasValue)
        {
            var status = request.FulfillmentStatus.Value;
            query = query.Where(order => order.FulfillmentStatus == status);
        }

        return await query
            .OrderByDescending(order => order.CreatedOnUtc)
            .ThenByDescending(order => order.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(order => new OrderListItemDto(
                order.Id,
                order.OrderNumber,
                order.UserId,
                order.Amount,
                order.Currency,
                order.Status.ToString(),
                order.FulfillmentStatus.ToString(),
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.PhoneNumber,
                order.CarrierName,
                order.TrackingNumber,
                order.CreatedOnUtc,
                order.UpdatedOnUtc))
            .ToArrayAsync(cancellationToken);
    }
}
