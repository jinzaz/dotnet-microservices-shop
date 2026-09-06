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
                order.SourceBasketId,
                order.SourceBasketVersion,
                order.SubtotalAmount,
                order.DiscountAmount,
                order.Amount,
                order.Currency,
                order.PaymentMethod,
                order.Status.ToString(),
                order.FulfillmentStatus.ToString(),
                order.ReservationId,
                order.PaymentId,
                order.CarrierCode,
                order.CarrierName,
                order.TrackingNumber,
                order.CancellationReason,
                order.PricingSnapshotId,
                order.PaymentDueOnUtc,
                new ShippingAddressDto(
                    order.ShippingAddress.SourceAddressId,
                    order.ShippingAddress.RecipientName,
                    order.ShippingAddress.PhoneNumber,
                    order.ShippingAddress.CountryCode,
                    order.ShippingAddress.Province,
                    order.ShippingAddress.City,
                    order.ShippingAddress.District,
                    order.ShippingAddress.StreetLine1,
                    order.ShippingAddress.StreetLine2,
                    order.ShippingAddress.PostalCode),
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
                order.FulfillmentStatusHistory
                    .OrderBy(history => history.OccurredOnUtc)
                    .Select(history => new OrderFulfillmentStatusHistoryDto(
                        history.FromStatus == null ? null : history.FromStatus.ToString(),
                        history.ToStatus.ToString(),
                        history.CarrierCode,
                        history.CarrierName,
                        history.TrackingNumber,
                        history.Note,
                        history.Actor,
                        history.OccurredOnUtc))
                    .ToArray(),
                order.CreatedOnUtc,
                order.UpdatedOnUtc,
                order.ShippedOnUtc,
                order.InTransitOnUtc,
                order.DeliveredOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
