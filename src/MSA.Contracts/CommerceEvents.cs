using MSA.EventBus.Abstractions;
namespace MSA.Contracts;

public sealed record OrderItemContract(string Sku, int Quantity, decimal UnitPrice);
public sealed class OrderSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required IReadOnlyList<OrderItemContract> Items { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string PaymentMethod { get; init; }
    public Guid? FlashSaleCampaignId { get; init; }
}
public sealed class InventoryReservedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid ReservationId { get; init; } public required decimal Amount { get; init; } public required string Currency { get; init; } public required string PaymentMethod { get; init; } }
public sealed class InventoryRejectedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required string Reason { get; init; } }
public sealed class ReleaseInventoryIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid ReservationId { get; init; } public required string Reason { get; init; } }
public sealed class CommitInventoryIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid ReservationId { get; init; } }
public sealed class InventoryReleasedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid ReservationId { get; init; } }
public sealed class PaymentRequestedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required string UserId { get; init; } public required decimal Amount { get; init; } public required string Currency { get; init; } public required string PaymentMethod { get; init; } }
public sealed class PaymentSucceededIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid PaymentId { get; init; } public required string ProviderTransactionId { get; init; } }
public sealed class PaymentFailedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required Guid PaymentId { get; init; } public required string Reason { get; init; } }
public sealed class OrderConfirmedIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required string UserId { get; init; } public required decimal Amount { get; init; } public required string Currency { get; init; } }
public sealed class OrderCancelledIntegrationEvent : IntegrationEvent { public required Guid OrderId { get; init; } public required string UserId { get; init; } public required string Reason { get; init; } }
public sealed class ProductChangedIntegrationEvent : IntegrationEvent { public required Guid ProductId { get; init; } public required string Sku { get; init; } public required string Name { get; init; } public required decimal Price { get; init; } public required string Currency { get; init; } public required bool IsActive { get; init; } }
public sealed class FlashSaleCampaignOpenedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required string Sku { get; init; }
    public required DateTimeOffset StartsOnUtc { get; init; }
    public required DateTimeOffset EndsOnUtc { get; init; }
    public required int PurchaseLimit { get; init; }
    public required string Strategy { get; init; }
}
public sealed class FlashSaleCampaignClosedIntegrationEvent : IntegrationEvent
{
    public required Guid CampaignId { get; init; }
    public required DateTimeOffset ClosedOnUtc { get; init; }
}
