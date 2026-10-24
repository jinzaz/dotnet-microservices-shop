using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.OrderService.Application.Realtime;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrder;

internal sealed class GetOrderQueryHandler(OrderDbContext db, IPaymentStatusGateway payments)
    : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(request, cancellationToken);
        if (order is null)
            return null;
        if (order.Status == nameof(OrderStatus.Confirmed))
            return await WithRefundsAsync(order, cancellationToken);
        if (order.Status != nameof(OrderStatus.AwaitingPayment))
            return order;

        // 只在「等待支付」时问一次支付服务：其余状态已经由事件落定，没必要多一次跨服务调用。
        var payment = await payments.GetAsync(order.Id, cancellationToken);
        return payment is null ? order : order with { LivePaymentStatus = payment.Status };
    }

    /// <summary>退款进度以售后单为准，查询时汇总，不在订单上冗余存一份。</summary>
    private async Task<OrderDto> WithRefundsAsync(OrderDto order, CancellationToken cancellationToken)
    {
        var refunded = await db.AfterSalesRequests
            .AsNoTracking()
            .Where(value => value.OrderId == order.Id && value.Status == AfterSalesStatus.Refunded)
            .SelectMany(value => value.Lines)
            .GroupBy(line => line.Sku)
            .Select(group => new { Sku = group.Key, Quantity = group.Sum(line => line.Quantity), Amount = group.Sum(line => line.RefundAmount) })
            .ToDictionaryAsync(line => line.Sku, StringComparer.Ordinal, cancellationToken);
        if (refunded.Count == 0)
            return order;
        return order with
        {
            RefundedAmount = refunded.Values.Sum(line => line.Amount),
            Lines = order.Lines
                .Select(line => line with { RefundedQuantity = refunded.GetValueOrDefault(line.Sku)?.Quantity ?? 0 })
                .ToArray()
        };
    }

    private Task<OrderDto?> LoadAsync(GetOrderQuery request, CancellationToken cancellationToken) =>
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
                    line.ProductId,
                    line.VariantId,
                    line.OfferId,
                    line.Sku,
                    line.ProductName,
                    line.VariantName,
                    line.VariantAttributesJson,
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
