using System.Globalization;
using Grpc.Core;
using MediatR;
using MSA.Grpc.Contracts.Payment;
using MSA.PaymentService.Application;

namespace MSA.PaymentService.Grpc;

/// <summary>
/// 服务间的支付状态查询。调用方持有 payment scope 的机器令牌，因此可以读任意订单的支付；
/// 面向终端用户的那条 REST 路径仍然按用户归属校验，两者共用同一个查询处理器。
/// </summary>
internal sealed class PaymentQueryGrpcService(ISender sender) : PaymentQuery.PaymentQueryBase
{
    public override async Task<GetOrderPaymentResponse> GetOrderPayment(
        GetOrderPaymentRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid order id is required."));

        var payment = await sender.Send(
            new GetPaymentByOrderQuery(orderId, UserId: string.Empty, CanReadAll: true),
            context.CancellationToken);
        if (payment is null)
            return new GetOrderPaymentResponse { Found = false };

        return new GetOrderPaymentResponse
        {
            Found = true,
            PaymentId = payment.Id.ToString(),
            Status = payment.Status,
            Amount = payment.Amount.ToString(CultureInfo.InvariantCulture),
            Currency = payment.Currency,
            Channel = payment.Channel,
            PaymentUrl = payment.PaymentUrl ?? string.Empty,
            ExpiresOnUtc = payment.ExpiresOnUtc.ToString("O", CultureInfo.InvariantCulture)
        };
    }
}
