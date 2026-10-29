using System.Text.Json;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application;

/// <summary>
/// Persisted order-fulfillment saga state. It is started by checkout (application layer) and advanced by
/// message handlers, so it lives here rather than in the integration layer.
/// </summary>
internal sealed record OrderFulfillmentSagaData(
    int SchemaVersion,
    string UserId,
    string PaymentMethod,
    Guid PricingSnapshotId,
    string PricingSnapshotHash,
    DateTimeOffset PaymentDueOnUtc,
    Guid? ReservationId = null,
    Guid? PaymentId = null,
    Guid? RefundRequestId = null,
    bool RequiresInventoryCompensation = false,
    bool InventoryCompensated = false,
    bool RequiresPaymentCompensation = false,
    bool PaymentCompensated = false,
    string? FailureReason = null,
    string? RedeliveryState = null,
    int Redeliveries = 0)
{
    public const string SagaType = "OrderFulfillment";
    public const string AwaitingInventory = "AwaitingInventory";
    public const string AwaitingPayment = "AwaitingPayment";
    public const string AwaitingInventoryCommit = "AwaitingInventoryCommit";
    public const string Compensating = "Compensating";
    public const string CompensatingInventory = "CompensatingInventory";
    public const string CompensatingRefund = "CompensatingRefund";

    public bool IsCompensationComplete =>
        (!RequiresInventoryCompensation || InventoryCompensated)
        && (!RequiresPaymentCompensation || PaymentCompensated);

    /// <summary>
    /// 当前状态下已经重发了几次。计数挂在状态名上：<see cref="RedeliveryState"/> 不是当前状态时视为 0，
    /// 进入新状态不需要在每个转移点手动清零。
    /// </summary>
    public int RedeliveriesIn(string state) => RedeliveryState == state ? Redeliveries : 0;

    public static OrderFulfillmentSagaData Start(Order order) =>
        new(
            1,
            order.UserId,
            order.PaymentMethod,
            order.PricingSnapshotId,
            order.PricingSnapshotHash,
            order.PaymentDueOnUtc);

    public static OrderFulfillmentSagaData Parse(string json) =>
        JsonSerializer.Deserialize<OrderFulfillmentSagaData>(json)
        ?? throw new InvalidOperationException("Order fulfillment saga data is invalid.");
}

public sealed class OrderSagaOptions
{
    public const string SectionName = "OrderSaga";

    /// <summary>超时重发（CommitInventory 和各类补偿）的退避基数：第 n 次重发后等 Base × 2^n。</summary>
    public TimeSpan RedeliveryBaseDelay { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RedeliveryMaxDelay { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>重发达到这个次数后每次都记 Error，但仍按上限间隔继续重发：补偿必须最终完成。</summary>
    public int RedeliveryErrorThreshold { get; set; } = 8;

    public TimeSpan RedeliveryDelay(int redeliveries) =>
        TimeSpan.FromTicks((long)Math.Min(
            RedeliveryBaseDelay.Ticks * Math.Pow(2, redeliveries),
            RedeliveryMaxDelay.Ticks));

    public static bool IsValid(OrderSagaOptions options) =>
        options.RedeliveryBaseDelay >= TimeSpan.FromSeconds(1)
        && options.RedeliveryMaxDelay >= options.RedeliveryBaseDelay
        && options.RedeliveryErrorThreshold >= 1;
}
