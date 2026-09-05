using MSA.PaymentCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MSA.PaymentService.Domain;

internal sealed class PaymentNotificationRecord
{
    private PaymentNotificationRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public PaymentChannel Channel { get; private set; }
    public string NotificationKey { get; private set; } = string.Empty;
    public string? ProviderNotificationId { get; private set; }
    public string MerchantTradeNo { get; private set; } = string.Empty;
    public string ProviderTransactionId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public PaymentNotificationStatus Status { get; private set; }
    public string? ConflictReason { get; private set; }
    public DateTimeOffset ReceivedOnUtc { get; private set; }
    public DateTimeOffset? ProcessedOnUtc { get; private set; }

    public static PaymentNotificationRecord Receive(
        Guid paymentId,
        PaymentChannel channel,
        string merchantTradeNo,
        string providerTransactionId,
        decimal amount,
        string currency,
        string? providerNotificationId,
        string? payloadHash,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantTradeNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);
        var normalizedCurrency = Payment.NormalizeCurrency(currency);
        var normalizedTransactionId = providerTransactionId.Trim();
        var normalizedNotificationId = string.IsNullOrWhiteSpace(providerNotificationId)
            ? null
            : providerNotificationId.Trim();
        var semanticFingerprint = CreateFingerprint(
            channel,
            merchantTradeNo.Trim(),
            normalizedTransactionId,
            amount,
            normalizedCurrency);
        var normalizedPayloadHash = NormalizePayloadHash(payloadHash) ?? semanticFingerprint;
        return new PaymentNotificationRecord
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            Channel = channel,
            NotificationKey = normalizedNotificationId is null
                ? $"payload:{normalizedPayloadHash}"
                : $"provider:{normalizedNotificationId}",
            ProviderNotificationId = normalizedNotificationId,
            MerchantTradeNo = merchantTradeNo.Trim(),
            ProviderTransactionId = normalizedTransactionId,
            Amount = amount,
            Currency = normalizedCurrency,
            Fingerprint = normalizedPayloadHash,
            Status = PaymentNotificationStatus.Received,
            ReceivedOnUtc = now
        };
    }

    public bool HasSamePayload(PaymentNotificationRecord other) =>
        PaymentId == other.PaymentId
        && string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    public void MarkApplied(DateTimeOffset now)
    {
        Status = PaymentNotificationStatus.Applied;
        ProcessedOnUtc = now;
    }

    public void MarkConflict(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = PaymentNotificationStatus.Conflict;
        ConflictReason = reason[..Math.Min(reason.Length, 2000)];
        ProcessedOnUtc = now;
    }

    private static string CreateFingerprint(
        PaymentChannel channel,
        string merchantTradeNo,
        string providerTransactionId,
        decimal amount,
        string currency)
    {
        var canonical = string.Join(
            '\n',
            ((int)channel).ToString(CultureInfo.InvariantCulture),
            merchantTradeNo,
            providerTransactionId,
            amount.ToString("G29", CultureInfo.InvariantCulture),
            currency);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? NormalizePayloadHash(string? payloadHash)
    {
        if (string.IsNullOrWhiteSpace(payloadHash))
            return null;
        var normalized = payloadHash.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Payload hash must be a SHA-256 hexadecimal value.", nameof(payloadHash));
        return normalized;
    }
}

internal enum PaymentNotificationStatus
{
    Received = 0,
    Applied = 1,
    Conflict = 2
}
