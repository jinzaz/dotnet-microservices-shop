namespace MSA.PaymentService.Domain;

/// <summary>
/// Durable order-level tombstone used when compensation arrives before the
/// corresponding payment request. It prevents a late request from creating a
/// new provider payment after Ordering has already cancelled the checkout.
/// </summary>
internal sealed class PaymentCancellationIntent
{
    private PaymentCancellationIntent()
    {
    }

    public Guid OrderId { get; private set; }
    public Guid RequestMessageId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset RequestedOnUtc { get; private set; }

    public static PaymentCancellationIntent Create(
        Guid orderId,
        Guid requestMessageId,
        string reason,
        DateTimeOffset requestedOnUtc)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        if (requestMessageId == Guid.Empty)
            throw new ArgumentException("Request message id is required.", nameof(requestMessageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        return new PaymentCancellationIntent
        {
            OrderId = orderId,
            RequestMessageId = requestMessageId,
            Reason = normalizedReason[..Math.Min(normalizedReason.Length, 2000)],
            RequestedOnUtc = requestedOnUtc
        };
    }
}
