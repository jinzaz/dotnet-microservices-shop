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

public sealed class AlipayGateway(IAlipayClient client, IOptions<PaylinksOptions> configured) : IPaymentGateway
{
    public PaymentChannel Channel => PaymentChannel.Alipay;
    public async Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest r, CancellationToken ct) { var c = configured.Value.Alipay; Ensure(c.AppId, c.AppPrivateKey, c.AlipayPublicKey); var req = new AlipayTradePreCreateRequest(); req.SetBodyModel(new AlipayTradePreCreateBodyModel { OutTradeNo = r.MerchantTradeNo, TotalAmount = r.Amount.ToString("0.00", CultureInfo.InvariantCulture), Subject = r.Subject, NotifyUrl = r.NotifyUrl }); var resp = await client.ExecuteAsync(req, new AlipayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, AppPrivateKey = c.AppPrivateKey, AlipayPublicKey = c.AlipayPublicKey }, cancellationToken: ct); if (!resp.IsSuccessful) throw new InvalidOperationException($"Alipay request failed: {resp.ErrorCode}."); return new(r.MerchantTradeNo, resp.QrCode, Channel); }
    private static void Ensure(params string[] values) { if (values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Alipay credentials are not configured."); }
}
public sealed class WeChatPayGateway(IWeChatPayClient client, IOptions<PaylinksOptions> configured) : IPaymentGateway
{
    public PaymentChannel Channel => PaymentChannel.WeChatPay;
    public async Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest r, CancellationToken ct) { var c = configured.Value.WeChatPay; Ensure(c.AppId, c.MchId, c.MchSerialNo, c.MchPrivateKey, c.APIv3Key); var req = new WeChatPayTransactionsNativeRequest(); req.SetBodyModel(new WeChatPayTransactionsNativeBodyModel { AppId = c.AppId, MchId = c.MchId, Description = r.Subject, OutTradeNo = r.MerchantTradeNo, TimeExpire = r.ExpiresOnUtc, NotifyUrl = r.NotifyUrl, Amount = new CommReqAmountInfo { Total = checked((int)decimal.Round(r.Amount * 100, 0)), Currency = r.Currency } }); var resp = await client.ExecuteAsync(req, new WeChatPayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, MchId = c.MchId, MchSerialNo = c.MchSerialNo, MchPrivateKey = c.MchPrivateKey, APIv3Key = c.APIv3Key, WeChatPayPublicKey = c.WeChatPayPublicKey, WeChatPayPublicKeyId = c.WeChatPayPublicKeyId }, ct); if (!resp.IsSuccessful) throw new InvalidOperationException("WeChat Pay request failed."); return new(r.MerchantTradeNo, resp.CodeUrl, Channel); }
    private static void Ensure(params string[] values) { if (values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("WeChat Pay credentials are not configured."); }
}
