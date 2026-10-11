using Microsoft.AspNetCore.Http.HttpResults;
using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;
using System.Security.Claims;

namespace MSA.PaymentService.Endpoints;

internal static class PaymentApiEndpoints
{
    internal static IEndpointRouteBuilder MapPaymentApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/payment/orders/{orderId:guid}", async Task<Results<Ok<PaymentDto>, NotFound>> (
            Guid orderId,
            ClaimsPrincipal user,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var subject = user.FindFirstValue("sub") ?? string.Empty;
            var payment = await sender.Send(
                new GetPaymentByOrderQuery(orderId, subject, false), cancellationToken);
            return payment is null ? TypedResults.NotFound() : TypedResults.Ok(payment);
        })
        .RequireAuthorization("payment-api")
        .WithName("GetPaymentByOrder");

        // 用户点“我已完成支付”或从收银台回跳时调用：没收到异步通知也能靠主动查询确认结果。
        endpoints.MapPost("/payment/orders/{orderId:guid}/sync", async Task<Results<Ok<PaymentDto>, NotFound>> (
            Guid orderId,
            ClaimsPrincipal user,
            ISender sender,
            PaymentGatewayResolver gateways,
            CancellationToken cancellationToken) =>
        {
            var subject = user.FindFirstValue("sub") ?? string.Empty;
            var payment = await sender.Send(
                new GetPaymentByOrderQuery(orderId, subject, false), cancellationToken);
            if (payment is null)
                return TypedResults.NotFound();
            var channel = Enum.Parse<PaymentChannel>(payment.Channel);
            if (payment.Status != nameof(PaymentStatus.Pending)
                || gateways.Resolve(channel) is not IPaymentQueryGateway query)
            {
                return TypedResults.Ok(payment);
            }

            var queried = await query.QueryPaymentAsync(
                Payment.CreateMerchantTradeNo(orderId), cancellationToken);
            if (queried is not { Succeeded: true })
                return TypedResults.Ok(payment);

            // 查询结果和回调走同一个确认命令：金额、币种、渠道、交易号冲突检查与幂等都在里面。
            await sender.Send(new ConfirmPaymentCommand(
                queried.MerchantTradeNo,
                queried.ProviderTransactionId,
                queried.Amount,
                queried.Currency,
                channel), cancellationToken);
            payment = await sender.Send(
                new GetPaymentByOrderQuery(orderId, subject, false), cancellationToken);
            return TypedResults.Ok(payment!);
        })
        .RequireAuthorization("payment-api")
        .WithName("SyncPaymentByOrder");

        return endpoints;
    }
}
