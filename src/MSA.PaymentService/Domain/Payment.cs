using MSA.Infrastrucute.Entities;
using MSA.PaymentCore;

namespace MSA.PaymentService.Domain;

public sealed class Payment : AggregateRoot<Guid>
{
    private Payment()
    {
    }

    public Payment(
        Guid id,
        Guid orderId,
        string userId,
        decimal amount,
        string currency,
        PaymentChannel channel)
        : this(id, orderId, userId, amount, currency, channel, DateTimeOffset.UtcNow)
    {
    }

    private Payment(
        Guid id,
        Guid orderId,
        string userId,
        decimal amount,
        string currency,
        PaymentChannel channel,
        DateTimeOffset createdOnUtc)
        : this(
            id,
            orderId,
            userId,
            amount,
            currency,
            channel,
            createdOnUtc,
            createdOnUtc.AddMinutes(15))
    {
    }

    public Payment(
        Guid id,
        Guid orderId,
        string userId,
        decimal amount,
        string currency,
        PaymentChannel channel,
        DateTimeOffset createdOnUtc,
        DateTimeOffset expiresOnUtc,
        Guid? pricingSnapshotId = null,
        string? pricingSnapshotHash = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Payment id is required.", nameof(id));
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (pricingSnapshotId.HasValue != !string.IsNullOrWhiteSpace(pricingSnapshotHash))
            throw new ArgumentException("Pricing snapshot id and hash must be supplied together.");
        Id = id;
        OrderId = orderId;
        UserId = userId.Trim();
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = NormalizeCurrency(currency);
        Channel = channel;
        Status = PaymentStatus.Pending;
        MerchantTradeNo = CreateMerchantTradeNo(orderId);
        CreatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        PricingSnapshotId = pricingSnapshotId;
        PricingSnapshotHash = NormalizeOptionalHash(pricingSnapshotHash);
    }

    public Guid OrderId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentChannel Channel { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string MerchantTradeNo { get; private set; } = string.Empty;
    public string? ProviderTransactionId { get; private set; }
    public string? PaymentUrl { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset ExpiresOnUtc { get; private set; }
    public DateTimeOffset? PaidOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public Guid? PricingSnapshotId { get; private set; }
    public string? PricingSnapshotHash { get; private set; }
    public long Version { get; private set; }

    public void EnsureMatchesRequest(
        string userId,
        decimal amount,
        string currency,
        PaymentChannel channel,
        Guid? pricingSnapshotId = null,
        string? pricingSnapshotHash = null,
        DateTimeOffset? paymentDueOnUtc = null)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        if (!string.Equals(UserId, userId.Trim(), StringComparison.Ordinal)
            || Amount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero)
            || !string.Equals(Currency, normalizedCurrency, StringComparison.Ordinal)
            || Channel != channel
            || PricingSnapshotId != pricingSnapshotId
            || !string.Equals(
                PricingSnapshotHash,
                NormalizeOptionalHash(pricingSnapshotHash),
                StringComparison.Ordinal)
            || paymentDueOnUtc.HasValue && ExpiresOnUtc != paymentDueOnUtc.Value)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} is already associated with a different payment request.");
        }
    }

    public string? GetSuccessfulNotificationConflict(
        string providerTransactionId,
        decimal amount,
        string currency,
        PaymentChannel channel)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId))
            return "The provider transaction id is missing.";
        if (Amount != amount)
            return $"Captured amount {amount} does not match expected amount {Amount}.";
        if (!string.Equals(Currency, NormalizeCurrency(currency), StringComparison.Ordinal))
            return $"Captured currency {currency} does not match expected currency {Currency}.";
        if (Channel != channel)
            return $"Notification channel {channel} does not match expected channel {Channel}.";
        if (ProviderTransactionId is not null
            && !string.Equals(ProviderTransactionId, providerTransactionId.Trim(), StringComparison.Ordinal))
        {
            return "The payment is already associated with a different provider transaction.";
        }
        if (Status == PaymentStatus.Closed)
            return "A closed payment cannot be completed.";

        return null;
    }

    public bool SetPaymentUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (Status != PaymentStatus.Pending)
            return false;
        if (PaymentUrl is not null)
        {
            if (!string.Equals(PaymentUrl, url, StringComparison.Ordinal))
                throw new InvalidOperationException("The provider returned a different URL for the same payment attempt.");
            return false;
        }

        PaymentUrl = url;
        Version++;
        return true;
    }

    public bool MarkPaid(string providerTransactionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);
        providerTransactionId = providerTransactionId.Trim();

        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunding or PaymentStatus.Refunded)
        {
            if (!string.Equals(ProviderTransactionId, providerTransactionId, StringComparison.Ordinal))
                throw new InvalidOperationException("The payment is already associated with a different provider transaction.");
            return false;
        }
        if (Status == PaymentStatus.Closed)
            throw new InvalidOperationException("A closed payment cannot be completed.");

        ProviderTransactionId = providerTransactionId;
        Status = PaymentStatus.Succeeded;
        PaidOnUtc = now;
        FailureReason = null;
        Version++;
        return true;
    }

    public bool Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status == PaymentStatus.Failed)
            return false;
        if (Status != PaymentStatus.Pending)
            return false;

        Status = PaymentStatus.Failed;
        FailureReason = Truncate(reason, 2000);
        Version++;
        return true;
    }

    public bool RequestCancellation(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status == PaymentStatus.Closed || Status == PaymentStatus.Cancelling)
            return false;
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Refunding or PaymentStatus.Refunded)
            return false;

        Status = PaymentStatus.Cancelling;
        FailureReason = Truncate(reason, 2000);
        Version++;
        return true;
    }

    public bool MarkCancelled(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status == PaymentStatus.Closed)
            return false;
        if (Status != PaymentStatus.Cancelling)
            throw new InvalidOperationException("Payment is not awaiting cancellation.");

        Status = PaymentStatus.Closed;
        FailureReason = Truncate(reason, 2000);
        ClosedOnUtc = now;
        Version++;
        return true;
    }

    public bool RequestRefund(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (Status == PaymentStatus.Refunding)
            return false;
        if (Status != PaymentStatus.Succeeded)
            throw new InvalidOperationException("Only a successful payment can be refunded.");
        if (RefundedAmount + amount > Amount)
            throw new InvalidOperationException("Refund amount exceeds the captured amount.");

        Status = PaymentStatus.Refunding;
        Version++;
        return true;
    }

    public void CompleteRefund(decimal amount)
    {
        if (Status != PaymentStatus.Refunding)
            throw new InvalidOperationException("Payment is not awaiting a refund.");
        if (amount <= 0 || RefundedAmount + amount > Amount)
            throw new InvalidOperationException("Refund amount is invalid.");

        RefundedAmount += amount;
        Status = RefundedAmount == Amount
            ? PaymentStatus.Refunded
            : PaymentStatus.Succeeded;
        Version++;
    }

    public void FailRefund(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != PaymentStatus.Refunding)
            return;

        Status = PaymentStatus.Succeeded;
        FailureReason = Truncate(reason, 2000);
        Version++;
    }

    public bool Expire(DateTimeOffset now, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != PaymentStatus.Pending || ExpiresOnUtc > now)
            return false;

        Status = PaymentStatus.Expired;
        FailureReason = Truncate(reason, 2000);
        Version++;
        return true;
    }

    public static string CreateMerchantTradeNo(Guid orderId) => orderId.ToString("N");

    public static string NormalizeCurrency(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter code.", nameof(currency));
        return normalized;
    }

    private static string? NormalizeOptionalHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Truncate(string value, int maxLength)
    {
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunding = 3,
    Refunded = 4,
    Closed = 5,
    Expired = 6,
    Cancelling = 7
}
