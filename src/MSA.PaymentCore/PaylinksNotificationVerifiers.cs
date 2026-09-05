using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Payments.Notify;
using Essensoft.Paylinks.WeChatPay.Client;
using Essensoft.Paylinks.WeChatPay.Core;
using Essensoft.Paylinks.WeChatPay.Payments.Notify;
using Microsoft.Extensions.Options;
using System.Globalization;
namespace MSA.PaymentCore;

public sealed class AlipayNotificationVerifier(IAlipayNotifyClient client, IOptions<PaylinksOptions> configured) : IPaymentNotificationVerifier
{
    public PaymentChannel Channel => PaymentChannel.Alipay;
    public async Task<VerifiedPaymentNotification> VerifyAsync(PaymentNotification n, CancellationToken ct) { var c = configured.Value.Alipay; var value = await client.ExecuteAsync<AlipayTradeStatusSyncNotify>(new Dictionary<string, string>(n.Parameters), new AlipayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, AppPrivateKey = c.AppPrivateKey, AlipayPublicKey = c.AlipayPublicKey }); var amount = decimal.Parse(value.TotalAmount, CultureInfo.InvariantCulture); return new(value.OutTradeNo, value.TradeNo, value.TradeStatus is "TRADE_SUCCESS" or "TRADE_FINISHED", amount, "CNY"); }
}
public sealed class WeChatPayNotificationVerifier(IWeChatPayNotifyClient client, IOptions<PaylinksOptions> configured) : IPaymentNotificationVerifier
{
    public PaymentChannel Channel => PaymentChannel.WeChatPay;
    public async Task<VerifiedPaymentNotification> VerifyAsync(PaymentNotification n, CancellationToken ct) { var c = configured.Value.WeChatPay; var headers = new WeChatPayHeaders(n.RequestId, n.Serial, n.Timestamp, n.Nonce, n.Signature, n.SignatureType); var value = await client.ExecuteAsync<WeChatPayTransactionSuccessNotify>(headers, n.Body, new WeChatPayClientOptions { ServerUrl = c.ServerUrl, AppId = c.AppId, MchId = c.MchId, MchSerialNo = c.MchSerialNo, MchPrivateKey = c.MchPrivateKey, APIv3Key = c.APIv3Key, WeChatPayPublicKey = c.WeChatPayPublicKey, WeChatPayPublicKeyId = c.WeChatPayPublicKeyId }); return new(value.OutTradeNo, value.TransactionId, value.TradeState == "SUCCESS", value.Amount.Total / 100m, value.Amount.Currency); }
}
