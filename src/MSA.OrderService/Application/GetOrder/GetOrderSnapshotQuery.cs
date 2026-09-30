using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Infrastructure;

namespace MSA.OrderService.Application.GetOrder;

/// <summary>订单的对外权威快照：金额、状态与定价快照，供支付核对与后台聚合读取。</summary>
public sealed record OrderSnapshotDto(
    Guid Id,
    string OrderNumber,
    string UserId,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string Currency,
    string Status,
    string FulfillmentStatus,
    string PaymentMethod,
    Guid PricingSnapshotId,
    string? PricingSnapshotHash,
    Guid? ReservationId,
    Guid? PaymentId,
    DateTimeOffset PaymentDueOnUtc,
    string? CarrierName,
    string? TrackingNumber);

/// <summary>
/// 服务间读取用的订单快照。调用方持有 order scope 的机器令牌，因此不按用户过滤；
/// 面向终端用户的那条路径仍然是 <see cref="GetOrderQuery"/>，按归属校验。
/// </summary>
public sealed record GetOrderSnapshotQuery(Guid OrderId) : IQuery<OrderSnapshotDto?>;

internal sealed class GetOrderSnapshotQueryHandler(OrderDbContext db)
    : IRequestHandler<GetOrderSnapshotQuery, OrderSnapshotDto?>
{
    public Task<OrderSnapshotDto?> Handle(GetOrderSnapshotQuery request, CancellationToken cancellationToken) =>
        db.Orders
            .AsNoTracking()
            .Where(order => order.Id == request.OrderId)
            .Select(order => new OrderSnapshotDto(
                order.Id,
                order.OrderNumber,
                order.UserId,
                order.SubtotalAmount,
                order.DiscountAmount,
                order.Amount,
                order.Currency,
                order.Status.ToString(),
                order.FulfillmentStatus.ToString(),
                order.PaymentMethod,
                order.PricingSnapshotId,
                order.PricingSnapshotHash,
                order.ReservationId,
                order.PaymentId,
                order.PaymentDueOnUtc,
                order.CarrierName,
                order.TrackingNumber))
            .SingleOrDefaultAsync(cancellationToken);
}
