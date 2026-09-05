using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrder;

internal sealed class GetOrderQueryHandler(OrderDbContext db)
    : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken) =>
        db.Orders
            .AsNoTracking()
            .Where(order =>
                order.Id == request.OrderId
                && (request.CanReadAll || order.UserId == request.UserId))
            .Select(order => new OrderDto(
                order.Id,
                order.OrderNumber,
                order.UserId,
                order.FlashSaleCampaignId,
                order.SubtotalAmount,
                order.DiscountAmount,
                order.Amount,
                order.Currency,
                order.PaymentMethod,
                order.Status.ToString(),
                order.ReservationId,
                order.PaymentId,
                order.CancellationReason,
                order.PricingSnapshotId,
                order.PaymentDueOnUtc,
                order.Lines.Select(line => new OrderLineDto(
                    line.Sku,
                    line.ProductName,
                    line.Quantity,
                    line.ListUnitPrice,
                    line.UnitPrice,
                    line.LineSubtotal,
                    line.DiscountAmount,
                    line.LineAmount)).ToArray(),
                order.Discounts.Select(discount => new OrderDiscountDto(
                    discount.PromotionId,
                    discount.PromotionRevision,
                    discount.CouponId,
                    discount.SourceType,
                    discount.Description,
                    discount.Amount)).ToArray(),
                order.CreatedOnUtc,
                order.UpdatedOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
