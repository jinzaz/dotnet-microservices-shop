namespace MSA.PaymentService.Application.Realtime;

/// <param name="Found">订单在订单服务里确实不存在时为 false。</param>
public sealed record OrderAuthoritySnapshot(
    bool Found,
    Guid OrderId,
    string UserId,
    decimal Amount,
    string Currency,
    string Status,
    Guid? PricingSnapshotId,
    string? PricingSnapshotHash);

/// <summary>
/// 订单的实时权威快照。返回 null 表示这次问不到（下游不可用或超时），
/// 此时按事件里的金额继续 —— 事件来自订单自己的 Outbox，是可信的，只是可能已经过期。
/// </summary>
public interface IOrderAuthority
{
    Task<OrderAuthoritySnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>
/// 建单收款前向订单核对这笔付款请求：金额与定价快照必须和订单此刻一致，订单也不能已经取消。
/// 事件来自订单自己的 Outbox，是可信的，但可能已经过期 —— 所以问不到就放行（返回 null），
/// 只有订单明确给出不一致的事实时才拒收。
/// </summary>
public sealed class OrderPaymentPrecheck(IOrderAuthority orders)
{
    /// <returns>拒收原因；null 表示核对通过或这次问不到。</returns>
    public async Task<string?> RejectAsync(
        Guid orderId,
        decimal amount,
        string currency,
        string? pricingSnapshotHash,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);
        if (order is null)
            return null;
        if (!order.Found)
            return "The order does not exist in Ordering.";
        if (string.Equals(order.Status, "Cancelled", StringComparison.Ordinal))
            return "The order was already cancelled.";
        if (order.Amount != amount || !string.Equals(order.Currency, currency, StringComparison.Ordinal))
            return $"The payable amount changed in Ordering ({order.Amount} {order.Currency}).";
        if (pricingSnapshotHash is not null
            && order.PricingSnapshotHash is not null
            && !string.Equals(order.PricingSnapshotHash, pricingSnapshotHash, StringComparison.Ordinal))
        {
            return "The pricing snapshot no longer matches the order.";
        }

        return null;
    }
}
