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
    public Guid? PricingSnapshotId { get; init; }
    public string? PricingSnapshotHash { get; init; }
    public DateTimeOffset? PaymentDueOnUtc { get; init; }
    public Guid? SourceBasketId { get; init; }
    public long? SourceBasketVersion { get; init; }
}

public sealed class InventoryReservedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string PaymentMethod { get; init; }
}

public sealed class InventoryRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}

public sealed class ReleaseInventoryIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
    public required string Reason { get; init; }
}

public sealed class CommitInventoryIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
}

public sealed class InventoryReleasedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
}

public sealed class InventoryCommittedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ReservationId { get; init; }
}

public sealed class PaymentRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string PaymentMethod { get; init; }
    public Guid? PricingSnapshotId { get; init; }
    public string? PricingSnapshotHash { get; init; }
    public DateTimeOffset? PaymentDueOnUtc { get; init; }
}

public sealed class PaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required string ProviderTransactionId { get; init; }
    public decimal? CapturedAmount { get; init; }
    public string? Currency { get; init; }
    public Guid? PricingSnapshotId { get; init; }
    public string? PricingSnapshotHash { get; init; }
}

public sealed class PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required string Reason { get; init; }
}

public sealed class CancelPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}

public sealed class PaymentCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
}

public sealed class RefundPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid RefundRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string Reason { get; init; }
}

public sealed class PaymentRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid RefundRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required Guid RefundId { get; init; }
}

public sealed class PaymentRefundFailedIntegrationEvent : IntegrationEvent
{
    public required Guid RefundRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid PaymentId { get; init; }
    public required Guid RefundId { get; init; }
    public required string Reason { get; init; }
}

public sealed class OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

public sealed class OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string UserId { get; init; }
    public required string Reason { get; init; }
}

public sealed class ProductChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public required string Currency { get; init; }
    public required bool IsActive { get; init; }

    // Optional additive fields keep old outbox payloads and rolling deployments valid.
    public Guid? CategoryId { get; init; }
    public string? LifecycleStatus { get; init; }
    public long Revision { get; init; }
    public DateTimeOffset? EffectiveOnUtc { get; init; }
}

public sealed record PromotionRuleContract(
    string RuleType,
    int SchemaVersion,
    string ConfigurationJson);

public sealed record PromotionBenefitContract(
    string BenefitType,
    int SchemaVersion,
    string ConfigurationJson);

public sealed record PromotionTargetContract(
    string TargetType,
    string? TargetKey,
    bool IsExcluded = false);

public sealed class PromotionSnapshotChangedIntegrationEvent : IntegrationEvent
{
    public required Guid PromotionId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string TriggerType { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset StartsOnUtc { get; init; }
    public required DateTimeOffset EndsOnUtc { get; init; }
    public int Priority { get; init; }
    public string StackPolicy { get; init; } = "Stackable";
    public string? ExclusiveGroup { get; init; }
    public long Revision { get; init; }
    public string DefinitionChecksum { get; init; } = string.Empty;
    public IReadOnlyList<PromotionRuleContract> Rules { get; init; } = [];
    public IReadOnlyList<PromotionBenefitContract> Benefits { get; init; } = [];
    public IReadOnlyList<PromotionTargetContract> Targets { get; init; } = [];
}

public sealed record CouponQuotaBucketContract(int BucketNumber, int Capacity);

public sealed class CouponSnapshotChangedIntegrationEvent : IntegrationEvent
{
    public required Guid CouponId { get; init; }
    public required Guid PromotionId { get; init; }
    public required string CodeDigest { get; init; }
    public required string MaskedCode { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset ValidFromUtc { get; init; }
    public required DateTimeOffset ValidUntilUtc { get; init; }
    public string? AssignedUserId { get; init; }
    public int? TotalLimit { get; init; }
    public int PerUserLimit { get; init; } = 1;
    public string QuotaStrategy { get; init; } = "SingleRow";
    public long Revision { get; init; }
    public IReadOnlyList<CouponQuotaBucketContract> QuotaBuckets { get; init; } = [];
}
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
