namespace MSA.PaymentService.Domain;

internal sealed class PaymentStatusHistory
{
    private PaymentStatusHistory()
    {
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public PaymentStatus? FromStatus { get; private set; }
    public PaymentStatus ToStatus { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public DateTimeOffset OccurredOnUtc { get; private set; }

    public static PaymentStatusHistory Record(
        Guid paymentId,
        PaymentStatus? fromStatus,
        PaymentStatus toStatus,
        string reasonCode,
        DateTimeOffset occurredOnUtc,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ReasonCode = reasonCode.Trim(),
            Details = details is null ? null : details[..Math.Min(details.Length, 2000)],
            OccurredOnUtc = occurredOnUtc
        };
    }
}
