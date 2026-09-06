namespace MSA.PaymentService.Domain;

/// <summary>
/// 订单级持久化取消墓碑：当补偿早于支付请求到达时，阻止迟到请求在订单已取消后创建渠道支付单。
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
