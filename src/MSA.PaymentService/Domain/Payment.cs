using MSA.Infrastrucute.Entities; using MSA.PaymentCore;
namespace MSA.PaymentService.Domain;
public sealed class Payment:AggregateRoot<Guid>
{
 private Payment(){} public Payment(Guid id,Guid orderId,string userId,decimal amount,string currency,PaymentChannel channel){if(amount<=0)throw new ArgumentOutOfRangeException(nameof(amount));Id=id;OrderId=orderId;UserId=userId;Amount=amount;Currency=currency;Channel=channel;Status=PaymentStatus.Pending;MerchantTradeNo=$"MSA{orderId:N}";CreatedOnUtc=DateTimeOffset.UtcNow;ExpiresOnUtc=CreatedOnUtc.AddMinutes(15);}
 public Guid OrderId{get;private set;}public string UserId{get;private set;}="";public decimal Amount{get;private set;}public string Currency{get;private set;}="";public PaymentChannel Channel{get;private set;}public PaymentStatus Status{get;private set;}public string MerchantTradeNo{get;private set;}="";public string? ProviderTransactionId{get;private set;}public string? PaymentUrl{get;private set;}public string? FailureReason{get;private set;}public DateTimeOffset CreatedOnUtc{get;private set;}public DateTimeOffset ExpiresOnUtc{get;private set;}public DateTimeOffset? PaidOnUtc{get;private set;}public long Version{get;private set;}
 public void SetPaymentUrl(string url){if(Status!=PaymentStatus.Pending)throw new InvalidOperationException("Payment is not pending.");PaymentUrl=url;Version++;}
 public bool MarkPaid(string transactionId,DateTimeOffset now){if(Status==PaymentStatus.Succeeded)return false;if(Status!=PaymentStatus.Pending)throw new InvalidOperationException("Payment cannot be completed.");ProviderTransactionId=transactionId;Status=PaymentStatus.Succeeded;PaidOnUtc=now;Version++;return true;}
 public bool Fail(string reason){if(Status==PaymentStatus.Failed)return false;if(Status!=PaymentStatus.Pending)return false;Status=PaymentStatus.Failed;FailureReason=reason;Version++;return true;}
}
public enum PaymentStatus{Pending,Succeeded,Failed,Refunding,Refunded,Closed}
