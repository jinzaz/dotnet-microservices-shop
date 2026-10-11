using Essensoft.Paylinks.Alipay.Client;
using Essensoft.Paylinks.Alipay.Payments.Model;
using Essensoft.Paylinks.Alipay.Payments.Notify;
using Essensoft.Paylinks.Alipay.Payments.Request;
using Microsoft.Extensions.Options;
using System.Globalization;
namespace MSA.PaymentCore;

/// <summary>
/// AliVibePay：支付宝 AI 网页应用收款（alipay.trade.page.pay）。对外仍是 <see cref="PaymentChannel.Alipay"/>，
/// 只在 <c>Paylinks:Alipay</c>（当面付预下单）未配置时注册，二者不会同时生效。
/// PaymentUrl 存的是需要浏览器提交的 POST 表单 HTML，不是可扫码的链接。
/// </summary>
public sealed class AliVibePayGateway(IAlipayClient client, IOptions<PaylinksOptions> configured)
    : IPaymentGateway, IPaymentCancellationGateway, IPaymentRefundGateway, IPaymentQueryGateway
{
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    public PaymentChannel Channel => PaymentChannel.Alipay;

    // alipay.trade.page.pay
    public async Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest r, CancellationToken ct)
    {
        var request = new AlipayTradePagePayRequest();
        // 本地没有公网 HTTPS 通知地址时不传 notify_url，靠主动查询兜底。
        if (r.NotifyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !IsLoopback(r.NotifyUrl))
            request.SetNotifyUrl(r.NotifyUrl);
        if (!string.IsNullOrWhiteSpace(r.ReturnUrl))
            request.SetReturnUrl(r.ReturnUrl);
        request.SetBizModel(new AlipayTradePagePayBizModel
        {
            OutTradeNo = r.MerchantTradeNo,
            TotalAmount = FormatAmount(r.Amount),
            Subject = r.Subject,
            ProductCode = "FAST_INSTANT_TRADE_PAY",
            TimeExpire = r.ExpiresOnUtc.ToOffset(BeijingOffset).DateTime
        });
        var form = await client.PageExecuteAsync(request, Options(), reqMethod: "POST");
        return new(r.MerchantTradeNo, form, Channel);
    }

    // alipay.trade.query
    public async Task<VerifiedPaymentNotification?> QueryPaymentAsync(string merchantTradeNo, CancellationToken ct)
    {
        var request = new AlipayTradeQueryRequest();
        request.SetBodyModel(new AlipayTradeQueryBodyModel { OutTradeNo = merchantTradeNo });
        var response = await client.ExecuteAsync(request, Options(), cancellationToken: ct);
        // 用户还没打开收银台时支付宝侧没有这笔交易。
        if (response.ErrorCode == "ACQ.TRADE_NOT_EXIST")
            return null;
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"AliVibePay query request failed: {response.ErrorCode}.");
        return new(
            response.OutTradeNo,
            response.TradeNo,
            response.TradeStatus is "TRADE_SUCCESS" or "TRADE_FINISHED",
            decimal.Parse(response.TotalAmount, CultureInfo.InvariantCulture),
            "CNY");
    }

    // alipay.trade.close
    public async Task<CancelPaymentResult> CancelPaymentAsync(CancelPaymentRequest r, CancellationToken ct)
    {
        var request = new AlipayTradeCloseRequest();
        request.SetBodyModel(new AlipayTradeCloseBodyModel { OutTradeNo = r.MerchantTradeNo });
        var response = await client.ExecuteAsync(request, Options(), cancellationToken: ct);
        // ponytail: 未打开收银台的交易在支付宝侧不存在，视为已关闭；表单在 time_expire 前仍可被提交，靠回调冲突记录兜底。
        if (!response.IsSuccessful && response.ErrorCode != "ACQ.TRADE_NOT_EXIST")
            throw new InvalidOperationException($"AliVibePay close request failed: {response.ErrorCode}.");
        return new CancelPaymentResult(r.MerchantTradeNo, Channel);
    }

    // alipay.trade.refund，未确认资金变动时再调 alipay.trade.fastpay.refund.query
    public async Task<RefundPaymentResult> RefundPaymentAsync(RefundPaymentRequest r, CancellationToken ct)
    {
        var request = new AlipayTradeRefundRequest();
        request.SetBodyModel(new AlipayTradeRefundBodyModel
        {
            OutTradeNo = r.MerchantTradeNo,
            TradeNo = r.ProviderTransactionId,
            OutRequestNo = r.MerchantRefundNo,
            RefundAmount = FormatAmount(r.RefundAmount),
            RefundReason = r.Reason
        });
        var response = await client.ExecuteAsync(request, Options(), cancellationToken: ct);
        if (!response.IsSuccessful)
            throw new InvalidOperationException($"AliVibePay refund request failed: {response.ErrorCode}.");
        if (response.FundChange == "Y")
            return new RefundPaymentResult(r.MerchantRefundNo, response.TradeNo, Channel, true);

        // fund_change 不是 Y 时以同一 out_request_no 的退款查询为准；未成功则由调度器按原参数重试。
        var query = new AlipayTradeFastPayRefundQueryRequest();
        query.SetBodyModel(new AlipayTradeFastPayRefundQueryBodyModel
        {
            OutTradeNo = r.MerchantTradeNo,
            OutRequestNo = r.MerchantRefundNo
        });
        var queried = await client.ExecuteAsync(query, Options(), cancellationToken: ct);
        if (!queried.IsSuccessful)
            throw new InvalidOperationException($"AliVibePay refund query failed: {queried.ErrorCode}.");
        return new RefundPaymentResult(r.MerchantRefundNo, response.TradeNo, Channel, queried.RefundStatus == "REFUND_SUCCESS");
    }

    private AlipayClientOptions Options() => AliVibePay.ClientOptions(configured.Value.AliVibePay);
    private static string FormatAmount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;
}

public sealed class AliVibePayNotificationVerifier(IAlipayNotifyClient client, IOptions<PaylinksOptions> configured) : IPaymentNotificationVerifier
{
    public PaymentChannel Channel => PaymentChannel.Alipay;

    public async Task<VerifiedPaymentNotification> VerifyAsync(PaymentNotification n, CancellationToken ct)
    {
        var c = configured.Value.AliVibePay;
        // SDK 验签失败会抛异常，回调返回非 success，支付宝会重试。
        var value = await client.ExecuteAsync<AlipayTradeStatusSyncNotify>(new Dictionary<string, string>(n.Parameters), AliVibePay.ClientOptions(c));
        if (!AliVibePay.BelongsToMerchant(n.Parameters, c))
            throw new InvalidOperationException("AliVibePay notification does not belong to the configured app or seller.");
        return new(value.OutTradeNo, value.TradeNo, AliVibePay.IsPaidNotification(n.Parameters), decimal.Parse(value.TotalAmount, CultureInfo.InvariantCulture), "CNY");
    }
}

public static class AliVibePay
{
    internal static AlipayClientOptions ClientOptions(PaylinksOptions.AlipaySettings c)
    {
        if (!c.IsConfigured)
            throw new InvalidOperationException("AliVibePay credentials are not configured.");
        return new() { ServerUrl = c.ServerUrl, AppId = c.AppId, AppPrivateKey = c.AppPrivateKey, AlipayPublicKey = c.AlipayPublicKey };
    }

    /// <summary>app_id 必须是本应用；配置了 SellerId 时 seller_id 也必须一致。预期值只取自运行时配置。</summary>
    public static bool BelongsToMerchant(IReadOnlyDictionary<string, string> p, PaylinksOptions.AlipaySettings c) =>
        p.GetValueOrDefault("app_id") == c.AppId
        && (string.IsNullOrWhiteSpace(c.SellerId) || p.GetValueOrDefault("seller_id") == c.SellerId);

    /// <summary>付款成功状态，且不是退款、关单、分账等带退款特征的通知。</summary>
    public static bool IsPaidNotification(IReadOnlyDictionary<string, string> p) =>
        p.GetValueOrDefault("trade_status") is "TRADE_SUCCESS" or "TRADE_FINISHED"
        && string.IsNullOrEmpty(p.GetValueOrDefault("out_biz_no"))
        && string.IsNullOrEmpty(p.GetValueOrDefault("gmt_refund"))
        && string.IsNullOrEmpty(p.GetValueOrDefault("refund_fee"));
}
