namespace MSA.InventoryService.Domain;

/// <summary>
/// 订单库存申请的持久化决策。订单标识是业务幂等键，请求摘要用于阻止同一键复用不同载荷。
/// </summary>
public sealed class InventoryRequestDecision
{
    private InventoryRequestDecision()
    {
    }

    private InventoryRequestDecision(
        Guid orderId,
        string requestHash,
        InventoryRequestDecisionStatus status,
        Guid? reservationId,
        string? rejectionReason,
        DateTimeOffset decidedOnUtc)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (requestHash.Length != 64)
            throw new ArgumentException("Request hash must be a SHA-256 hexadecimal value.", nameof(requestHash));

        if (status == InventoryRequestDecisionStatus.Accepted && !reservationId.HasValue)
            throw new ArgumentException("An accepted decision requires a reservation id.", nameof(reservationId));
        if (status == InventoryRequestDecisionStatus.Rejected && string.IsNullOrWhiteSpace(rejectionReason))
            throw new ArgumentException("A rejected decision requires a reason.", nameof(rejectionReason));
        if (rejectionReason?.Length > 1000)
            throw new ArgumentException("Rejection reason cannot exceed 1000 characters.", nameof(rejectionReason));

        OrderId = orderId;
        RequestHash = requestHash;
        Status = status;
        ReservationId = reservationId;
        RejectionReason = rejectionReason?.Trim();
        DecidedOnUtc = decidedOnUtc;
    }

    public Guid OrderId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public InventoryRequestDecisionStatus Status { get; private set; }
    public Guid? ReservationId { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset DecidedOnUtc { get; private set; }

    public bool IsAccepted => Status == InventoryRequestDecisionStatus.Accepted;

    public static InventoryRequestDecision Accept(
        Guid orderId,
        string requestHash,
        Guid reservationId,
        DateTimeOffset decidedOnUtc) =>
        new(
            orderId,
            requestHash,
            InventoryRequestDecisionStatus.Accepted,
            reservationId,
            null,
            decidedOnUtc);

    public static InventoryRequestDecision Reject(
        Guid orderId,
        string requestHash,
        string reason,
        DateTimeOffset decidedOnUtc) =>
        new(
            orderId,
            requestHash,
            InventoryRequestDecisionStatus.Rejected,
            null,
            reason,
            decidedOnUtc);

    public void EnsureMatches(string requestHash)
    {
        if (!string.Equals(RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Order {OrderId} was resubmitted with a different inventory request payload.");
        }
    }
}

public enum InventoryRequestDecisionStatus
{
    Accepted = 0,
    Rejected = 1
}
