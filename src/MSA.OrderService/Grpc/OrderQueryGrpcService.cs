using System.Globalization;
using Grpc.Core;
using GrpcStatus = Grpc.Core.Status;
using GrpcStatusCode = Grpc.Core.StatusCode;
using MediatR;
using MSA.Grpc.Contracts.Order;
using MSA.OrderService.Application.GetOrder;

namespace MSA.OrderService.Grpc;

/// <summary>
/// 传输层适配：请求转成查询交给应用层，结果映射回契约。业务规则只在 Application 里有一份。
/// </summary>
internal sealed class OrderQueryGrpcService(ISender sender) : OrderQuery.OrderQueryBase
{
    public override async Task<GetOrderSnapshotResponse> GetOrderSnapshot(
        GetOrderSnapshotRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
            throw new RpcException(new GrpcStatus(GrpcStatusCode.InvalidArgument, "A valid order id is required."));

        var order = await sender.Send(new GetOrderSnapshotQuery(orderId), context.CancellationToken);
        if (order is null)
            return new GetOrderSnapshotResponse { Found = false };

        return new GetOrderSnapshotResponse
        {
            Found = true,
            OrderId = order.Id.ToString(),
            OrderNumber = order.OrderNumber,
            UserId = order.UserId,
            // 金额按不变文化格式化，跨服务传递不受本地化影响。
            Amount = order.Amount.ToString(CultureInfo.InvariantCulture),
            SubtotalAmount = order.SubtotalAmount.ToString(CultureInfo.InvariantCulture),
            DiscountAmount = order.DiscountAmount.ToString(CultureInfo.InvariantCulture),
            Currency = order.Currency,
            Status = order.Status,
            FulfillmentStatus = order.FulfillmentStatus,
            PaymentMethod = order.PaymentMethod,
            PricingSnapshotId = order.PricingSnapshotId.ToString(),
            PricingSnapshotHash = order.PricingSnapshotHash ?? string.Empty,
            ReservationId = order.ReservationId?.ToString() ?? string.Empty,
            PaymentId = order.PaymentId?.ToString() ?? string.Empty,
            PaymentDueOnUtc = order.PaymentDueOnUtc.ToString("O", CultureInfo.InvariantCulture),
            CarrierName = order.CarrierName ?? string.Empty,
            TrackingNumber = order.TrackingNumber ?? string.Empty
        };
    }
}
