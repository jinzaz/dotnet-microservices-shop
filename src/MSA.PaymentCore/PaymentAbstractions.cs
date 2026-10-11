namespace MSA.PaymentCore;

public enum PaymentChannel { Alipay, WeChatPay }
public sealed record CreatePaymentRequest(string MerchantTradeNo, decimal Amount, string Currency, string Subject, string NotifyUrl, DateTimeOffset ExpiresOnUtc, string? ReturnUrl = null);
public sealed record CreatePaymentResult(string MerchantTradeNo, string PaymentUrl, PaymentChannel Channel);
public interface IPaymentGateway { PaymentChannel Channel { get; } Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken); }
public sealed record CancelPaymentRequest(string MerchantTradeNo);
public sealed record CancelPaymentResult(string MerchantTradeNo, PaymentChannel Channel);
public interface IPaymentCancellationGateway
{
    Task<CancelPaymentResult> CancelPaymentAsync(
        CancelPaymentRequest request,
        CancellationToken cancellationToken);
}
public sealed record RefundPaymentRequest(
    string MerchantTradeNo,
    string ProviderTransactionId,
    string MerchantRefundNo,
    decimal RefundAmount,
    decimal TotalAmount,
    string Currency,
    string Reason);
public sealed record RefundPaymentResult(
    string MerchantRefundNo,
    string? ProviderRefundId,
    PaymentChannel Channel,
    bool Completed);
public interface IPaymentRefundGateway
{
    Task<RefundPaymentResult> RefundPaymentAsync(
        RefundPaymentRequest request,
        CancellationToken cancellationToken);
}
/// <summary>主动查询渠道交易状态，用于没收到异步通知时补偿；交易在渠道侧不存在时返回 null。</summary>
public interface IPaymentQueryGateway
{
    Task<VerifiedPaymentNotification?> QueryPaymentAsync(
        string merchantTradeNo,
        CancellationToken cancellationToken);
}
public sealed record PaymentNotification(IReadOnlyDictionary<string, string> Parameters, string Body, string RequestId = "", string Serial = "", string Timestamp = "", string Nonce = "", string Signature = "", string SignatureType = "");
public sealed record VerifiedPaymentNotification(string MerchantTradeNo, string ProviderTransactionId, bool Succeeded, decimal Amount, string Currency);
public interface IPaymentNotificationVerifier { PaymentChannel Channel { get; } Task<VerifiedPaymentNotification> VerifyAsync(PaymentNotification notification, CancellationToken cancellationToken); }
public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways)
{
    public IPaymentGateway Resolve(PaymentChannel channel) => gateways.SingleOrDefault(x => x.Channel == channel) ?? throw new NotSupportedException($"Payment channel {channel} is not registered.");
}
public sealed class PaylinksOptions
{
    public AlipaySettings Alipay { get; set; } = new(); public WeChatPaySettings WeChatPay { get; set; } = new();
    /// <summary>支付宝 AI 网页应用收款（alipay.trade.page.pay）。仅当 <see cref="Alipay"/> 未配置时才启用，前端仍只看到“支付宝”。</summary>
    public AlipaySettings AliVibePay { get; set; } = new();
    public sealed class AlipaySettings
    {
        public string ServerUrl { get; set; } = "https://openapi.alipay.com"; public string AppId { get; set; } = ""; public string AppPrivateKey { get; set; } = ""; public string AlipayPublicKey { get; set; } = "";
        /// <summary>收款支付宝账号的 PID（2088 开头）；配置后异步通知会校验 seller_id。</summary>
        public string SellerId { get; set; } = "";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppPrivateKey) && !string.IsNullOrWhiteSpace(AlipayPublicKey);
    }
    public sealed class WeChatPaySettings { public string ServerUrl { get; set; } = "https://api.mch.weixin.qq.com"; public string AppId { get; set; } = ""; public string MchId { get; set; } = ""; public string MchSerialNo { get; set; } = ""; public string MchPrivateKey { get; set; } = ""; public string APIv3Key { get; set; } = ""; public string? WeChatPayPublicKey { get; set; } public string? WeChatPayPublicKeyId { get; set; } }
}
