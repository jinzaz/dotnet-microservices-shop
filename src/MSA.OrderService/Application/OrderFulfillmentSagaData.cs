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
    string? FailureReason = null)
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
