using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Payments.Model;
using Essensoft.Paylinks.Alipay.Payments.Request;
using Essensoft.Paylinks.WeChatPay.Client;
using Essensoft.Paylinks.WeChatPay.Payments.Domain;
using Essensoft.Paylinks.WeChatPay.Payments.Model;
using Essensoft.Paylinks.WeChatPay.Payments.Request;
using Microsoft.Extensions.Options;
using System.Globalization;
namespace MSA.PaymentCore;

public sealed class AlipayGateway(IAlipayClient client, IOptions<PaylinksOptions> configured)
    : IPaymentGateway, IPaymentCancellationGateway, IPaymentRefundGateway
{
    public PaymentChannel Channel => PaymentChannel.Alipay;
    public async Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest r, CancellationToken ct) { var c = configured.Value.Alipay; Ensure(c.AppId, c.AppPrivateKey, c.AlipayPublicKey); var req = new AlipayTradePreCreateRequest(); req.SetBodyModel(new AlipayTradePreCreateBodyModel { OutTradeNo = r.MerchantTradeNo, TotalAmount = r.Amount.ToString("0.00", CultureInfo.InvariantCulture), Subject = r.Subject, NotifyUrl = r.NotifyUrl }); var resp = await client.ExecuteAsync(req, new AlipayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, AppPrivateKey = c.AppPrivateKey, AlipayPublicKey = c.AlipayPublicKey }, cancellationToken: ct); if (!resp.IsSuccessful) throw new InvalidOperationException($"Alipay request failed: {resp.ErrorCode}."); return new(r.MerchantTradeNo, resp.QrCode, Channel); }
    public async Task<CancelPaymentResult> CancelPaymentAsync(CancelPaymentRequest r, CancellationToken ct)
    {
        var c = configured.Value.Alipay;
        Ensure(c.AppId, c.AppPrivateKey, c.AlipayPublicKey);
        var request = new AlipayTradeCloseRequest();
        request.SetBodyModel(new AlipayTradeCloseBodyModel { OutTradeNo = r.MerchantTradeNo });
        var response = await client.ExecuteAsync(
            request,
            new AlipayClientOptions
            {
                ServerUrl = c.ServerUrl,
                AppId = c.AppId,
                AppPrivateKey = c.AppPrivateKey,
                AlipayPublicKey = c.AlipayPublicKey
            },
            cancellationToken: ct);
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"Alipay close request failed: {response.ErrorCode}.");
        return new CancelPaymentResult(r.MerchantTradeNo, Channel);
    }
    public async Task<RefundPaymentResult> RefundPaymentAsync(RefundPaymentRequest r, CancellationToken ct)
    {
        var c = configured.Value.Alipay;
        Ensure(c.AppId, c.AppPrivateKey, c.AlipayPublicKey);
        var request = new AlipayTradeRefundRequest();
        request.SetBodyModel(new AlipayTradeRefundBodyModel
        {
            OutTradeNo = r.MerchantTradeNo,
            TradeNo = r.ProviderTransactionId,
            OutRequestNo = r.MerchantRefundNo,
            RefundAmount = r.RefundAmount.ToString("0.00", CultureInfo.InvariantCulture),
            RefundReason = r.Reason
        });
        var response = await client.ExecuteAsync(
            request,
            new AlipayClientOptions
            {
                ServerUrl = c.ServerUrl,
                AppId = c.AppId,
                AppPrivateKey = c.AppPrivateKey,
                AlipayPublicKey = c.AlipayPublicKey
            },
            cancellationToken: ct);
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"Alipay refund request failed: {response.ErrorCode}.");
        return new RefundPaymentResult(r.MerchantRefundNo, response.TradeNo, Channel, true);
    }
    private static void Ensure(params string[] values) { if (values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Alipay credentials are not configured."); }
}
public sealed class WeChatPayGateway(IWeChatPayClient client, IOptions<PaylinksOptions> configured)
    : IPaymentGateway, IPaymentCancellationGateway, IPaymentRefundGateway
{
    public PaymentChannel Channel => PaymentChannel.WeChatPay;
    public async Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest r, CancellationToken ct) { var c = configured.Value.WeChatPay; Ensure(c.AppId, c.MchId, c.MchSerialNo, c.MchPrivateKey, c.APIv3Key); var req = new WeChatPayTransactionsNativeRequest(); req.SetBodyModel(new WeChatPayTransactionsNativeBodyModel { AppId = c.AppId, MchId = c.MchId, Description = r.Subject, OutTradeNo = r.MerchantTradeNo, TimeExpire = r.ExpiresOnUtc, NotifyUrl = r.NotifyUrl, Amount = new CommReqAmountInfo { Total = checked((int)decimal.Round(r.Amount * 100, 0)), Currency = r.Currency } }); var resp = await client.ExecuteAsync(req, new WeChatPayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, MchId = c.MchId, MchSerialNo = c.MchSerialNo, MchPrivateKey = c.MchPrivateKey, APIv3Key = c.APIv3Key, WeChatPayPublicKey = c.WeChatPayPublicKey, WeChatPayPublicKeyId = c.WeChatPayPublicKeyId }, ct); if (!resp.IsSuccessful) throw new InvalidOperationException("WeChat Pay request failed."); return new(r.MerchantTradeNo, resp.CodeUrl, Channel); }
    public async Task<CancelPaymentResult> CancelPaymentAsync(CancelPaymentRequest r, CancellationToken ct)
    {
        var c = configured.Value.WeChatPay;
        Ensure(c.AppId, c.MchId, c.MchSerialNo, c.MchPrivateKey, c.APIv3Key);
        var request = new WeChatPayCloseByOutTradeNoRequest { OutTradeNo = r.MerchantTradeNo };
        request.SetBodyModel(new WeChatPayCloseByOutTradeNoBodyModel { MchId = c.MchId });
        var response = await client.ExecuteAsync(
            request,
            CreateOptions(c),
            ct);
        if (!response.IsSuccessful)
            throw new InvalidOperationException("WeChat Pay close request failed.");
        return new CancelPaymentResult(r.MerchantTradeNo, Channel);
    }
    public async Task<RefundPaymentResult> RefundPaymentAsync(RefundPaymentRequest r, CancellationToken ct)
    {
        var c = configured.Value.WeChatPay;
        Ensure(c.AppId, c.MchId, c.MchSerialNo, c.MchPrivateKey, c.APIv3Key);
        var request = new WeChatPayRefundRequest();
        request.SetBodyModel(new WeChatPayRefundBodyModel
        {
            TransactionId = r.ProviderTransactionId,
            OutTradeNo = r.MerchantTradeNo,
            OutRefundNo = r.MerchantRefundNo,
            Reason = r.Reason,
            Amount = new RefundAmount
            {
                Refund = ToMinorUnits(r.RefundAmount),
                Total = ToMinorUnits(r.TotalAmount),
                Currency = r.Currency
            }
        });
        var response = await client.ExecuteAsync(request, CreateOptions(c), ct);
        if (!response.IsSuccessful)
            throw new InvalidOperationException("WeChat Pay refund request failed.");
        var completed = string.Equals(response.Status.ToString(), "Success", StringComparison.OrdinalIgnoreCase);
        return new RefundPaymentResult(r.MerchantRefundNo, response.RefundId, Channel, completed);
    }
    private static WeChatPayClientOptions CreateOptions(PaylinksOptions.WeChatPaySettings c) =>
        new()
        {
            ServerUrl = c.ServerUrl,
            AppId = c.AppId,
            MchId = c.MchId,
            MchSerialNo = c.MchSerialNo,
            MchPrivateKey = c.MchPrivateKey,
            APIv3Key = c.APIv3Key,
            WeChatPayPublicKey = c.WeChatPayPublicKey,
            WeChatPayPublicKeyId = c.WeChatPayPublicKeyId
        };
    private static int ToMinorUnits(decimal value) =>
        checked((int)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero));
    private static void Ensure(params string[] values) { if (values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("WeChat Pay credentials are not configured."); }
}
