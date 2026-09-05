namespace MSA.PaymentService.Domain;

internal sealed class PaymentRefund
{
    private PaymentRefund()
    {
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid RefundRequestId { get; private set; }
    public Guid RequestMessageId { get; private set; }
    public string MerchantRefundNo { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public PaymentRefundStatus Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? CompletedOnUtc { get; private set; }
    public long Version { get; private set; }

    public static PaymentRefund Request(
        Guid paymentId,
        Guid orderId,
        Guid refundRequestId,
        Guid requestMessageId,
        decimal amount,
        string currency,
        string reason,
        DateTimeOffset now)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        if (refundRequestId == Guid.Empty)
            throw new ArgumentException("Refund request id is required.", nameof(refundRequestId));
        if (requestMessageId == Guid.Empty)
            throw new ArgumentException("Request message id is required.", nameof(requestMessageId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        var id = Guid.NewGuid();
        return new PaymentRefund
        {
            Id = id,
            PaymentId = paymentId,
            OrderId = orderId,
            RefundRequestId = refundRequestId,
            RequestMessageId = requestMessageId,
            MerchantRefundNo = refundRequestId.ToString("N"),
            Amount = amount,
            Currency = Payment.NormalizeCurrency(currency),
            Reason = normalizedReason[..Math.Min(normalizedReason.Length, 500)],
            Status = PaymentRefundStatus.Requested,
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };
    }

    public void EnsureMatchesRequest(
        Guid paymentId,
        Guid orderId,
        decimal amount,
        string currency,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        normalizedReason = normalizedReason[..Math.Min(normalizedReason.Length, 500)];
        if (PaymentId != paymentId
            || OrderId != orderId
            || Amount != amount
            || !string.Equals(
                Currency,
                Payment.NormalizeCurrency(currency),
                StringComparison.Ordinal)
            || !string.Equals(Reason, normalizedReason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refund request {RefundRequestId} was reused with different payment details.");
        }
    }

    public void MarkSucceeded(string? providerRefundId, DateTimeOffset now)
    {
        if (Status == PaymentRefundStatus.Succeeded)
            return;
        if (Status != PaymentRefundStatus.Requested)
            throw new InvalidOperationException("Refund is not pending.");

        ProviderRefundId = string.IsNullOrWhiteSpace(providerRefundId)
            ? null
            : providerRefundId.Trim();
        Status = PaymentRefundStatus.Succeeded;
        FailureReason = null;
        CompletedOnUtc = now;
        UpdatedOnUtc = now;
        Version++;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != PaymentRefundStatus.Requested)
            return;

        Status = PaymentRefundStatus.Failed;
        var normalizedReason = reason.Trim();
        FailureReason = normalizedReason[..Math.Min(normalizedReason.Length, 2000)];
        CompletedOnUtc = now;
        UpdatedOnUtc = now;
        Version++;
    }
}

internal enum PaymentRefundStatus
{
    Requested = 0,
    Succeeded = 1,
    Failed = 2
}
