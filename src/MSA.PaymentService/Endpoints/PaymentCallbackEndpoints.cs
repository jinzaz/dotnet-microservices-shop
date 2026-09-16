using MSA.PaymentCore;
using MSA.PaymentService.Application;
using MSA.PaymentService.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSA.PaymentService.Endpoints;

internal static class PaymentCallbackEndpoints
{
    internal static IEndpointRouteBuilder MapPaymentCallbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/payment/callbacks/alipay", async (
            HttpRequest request,
            IEnumerable<IPaymentNotificationVerifier> verifiers,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var values = form.ToDictionary(value => value.Key, value => value.Value.ToString());
            var verified = await verifiers.Single(value => value.Channel == PaymentChannel.Alipay)
                .VerifyAsync(new PaymentNotification(values, string.Empty), cancellationToken);
            if (!verified.Succeeded)
                return Results.BadRequest();

            await sender.Send(new ConfirmPaymentCommand(
                verified.MerchantTradeNo,
                verified.ProviderTransactionId,
                verified.Amount,
                verified.Currency,
                PaymentChannel.Alipay,
                values.GetValueOrDefault("notify_id"),
                ComputeSha256(string.Join(
                    '&',
                    values.OrderBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => $"{value.Key}={value.Value}")))), cancellationToken);
            return Results.Text("success");
        }).AllowAnonymous();

        endpoints.MapPost("/payment/callbacks/wechatpay", async (
            HttpRequest request,
            IEnumerable<IPaymentNotificationVerifier> verifiers,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            string Header(string name) => request.Headers[name].ToString();
            var notification = new PaymentNotification(
                new Dictionary<string, string>(), body, Header("Request-ID"), Header("Wechatpay-Serial"),
                Header("Wechatpay-Timestamp"), Header("Wechatpay-Nonce"), Header("Wechatpay-Signature"),
                Header("Wechatpay-Signature-Type"));
            var verified = await verifiers.Single(value => value.Channel == PaymentChannel.WeChatPay)
                .VerifyAsync(notification, cancellationToken);
            if (!verified.Succeeded)
                return Results.BadRequest();

            await sender.Send(new ConfirmPaymentCommand(
                verified.MerchantTradeNo,
                verified.ProviderTransactionId,
                verified.Amount,
                verified.Currency,
                PaymentChannel.WeChatPay,
                TryReadNotificationId(body),
                ComputeSha256(body)), cancellationToken);
            return Results.Json(new { code = "SUCCESS", message = "成功" });
        }).AllowAnonymous();

        return endpoints;
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? TryReadNotificationId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
