namespace MSA.PaymentCore;
public enum PaymentChannel { Alipay, WeChatPay }
public sealed record CreatePaymentRequest(string MerchantTradeNo, decimal Amount, string Currency, string Subject, string NotifyUrl, DateTimeOffset ExpiresOnUtc);
public sealed record CreatePaymentResult(string MerchantTradeNo, string PaymentUrl, PaymentChannel Channel);
public interface IPaymentGateway { PaymentChannel Channel { get; } Task<CreatePaymentResult> CreateNativePaymentAsync(CreatePaymentRequest request,CancellationToken cancellationToken); }
public sealed record PaymentNotification(IReadOnlyDictionary<string,string> Parameters,string Body,string RequestId="",string Serial="",string Timestamp="",string Nonce="",string Signature="",string SignatureType="");
public sealed record VerifiedPaymentNotification(string MerchantTradeNo,string ProviderTransactionId,bool Succeeded,decimal Amount,string Currency);
public interface IPaymentNotificationVerifier { PaymentChannel Channel{get;} Task<VerifiedPaymentNotification> VerifyAsync(PaymentNotification notification,CancellationToken cancellationToken); }
public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways)
{
    public IPaymentGateway Resolve(PaymentChannel channel)=>gateways.SingleOrDefault(x=>x.Channel==channel)??throw new NotSupportedException($"Payment channel {channel} is not registered.");
}
public sealed class PaylinksOptions
{
    public AlipaySettings Alipay { get; set; }=new(); public WeChatPaySettings WeChatPay { get; set; }=new();
    public sealed class AlipaySettings { public string ServerUrl{get;set;}="https://openapi.alipay.com";public string AppId{get;set;}="";public string AppPrivateKey{get;set;}="";public string AlipayPublicKey{get;set;}=""; }
    public sealed class WeChatPaySettings { public string ServerUrl{get;set;}="https://api.mch.weixin.qq.com";public string AppId{get;set;}="";public string MchId{get;set;}="";public string MchSerialNo{get;set;}="";public string MchPrivateKey{get;set;}="";public string APIv3Key{get;set;}="";public string? WeChatPayPublicKey{get;set;}public string? WeChatPayPublicKeyId{get;set;} }
}
