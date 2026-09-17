using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.Infrastrucute.Entities;

namespace MSA.CatalogService.Infrastructure;

internal sealed class OutboxCatalogSnapshotPublisher(IOutboxStore outbox) : ICatalogSnapshotPublisher
{
    public void Publish(Product product)
    {
        if (!HasUnsavedSnapshotChange(product))
            return;
        foreach (var variant in product.Variants)
            outbox.AddIntegrationEvent(ToVariantEvent(product, variant, product.IsActive && variant.IsActive));
    }

    // A removed variant no longer raises its own snapshot, so its tombstone is always emitted.
    public void PublishUnavailable(Product product, ProductVariant variant) =>
        outbox.AddIntegrationEvent(ToVariantEvent(product, variant, isActive: false));

    public void Publish(Promotion promotion)
    {
        if (HasUnsavedSnapshotChange(promotion))
            outbox.AddIntegrationEvent(ToEvent(promotion));
    }

    public void Publish(Coupon coupon)
    {
        if (HasUnsavedSnapshotChange(coupon))
            outbox.AddIntegrationEvent(ToEvent(coupon));
    }

    /// <summary>
    /// Aggregates raise a snapshot domain event only for effective changes, and events are cleared on save.
    /// Idempotent no-op commands therefore do not flood projections with duplicate revisions.
    /// </summary>
    private static bool HasUnsavedSnapshotChange<TId>(AggregateRoot<TId> aggregate)
        where TId : notnull =>
        aggregate.DomainEvents.Count > 0;

    private static PromotionSnapshotChangedIntegrationEvent ToEvent(Promotion promotion) => new()
    {
        PromotionId = promotion.Id,
        Code = promotion.NormalizedCode,
        Name = promotion.Name,
        TriggerType = promotion.TriggerType.ToString(),
        Status = promotion.Status.ToString(),
        StartsOnUtc = promotion.StartsOnUtc,
        EndsOnUtc = promotion.EndsOnUtc,
        Priority = promotion.Priority,
        StackPolicy = promotion.StackPolicy.ToString(),
        ExclusiveGroup = promotion.ExclusiveGroup,
        Revision = promotion.Revision,
        DefinitionChecksum = promotion.DefinitionChecksum,
        Rules = promotion.Rules
            .OrderBy(rule => rule.Sequence)
            .Select(rule => new PromotionRuleContract(
                rule.RuleType,
                rule.SchemaVersion,
                rule.ConfigurationJson))
            .ToArray(),
        Benefits = promotion.Benefits
            .OrderBy(benefit => benefit.Sequence)
            .Select(benefit => new PromotionBenefitContract(
                benefit.BenefitType,
                benefit.SchemaVersion,
                benefit.ConfigurationJson))
            .ToArray(),
        Targets = promotion.Targets
            .OrderBy(target => target.Sequence)
            .Select(target => new PromotionTargetContract(
                target.TargetType,
                string.Equals(target.TargetType, "Sku", StringComparison.OrdinalIgnoreCase)
                    ? target.TargetKey?.Trim().ToUpperInvariant()
                    : target.TargetKey,
                target.IsExcluded))
            .ToArray()
    };

    private static CouponSnapshotChangedIntegrationEvent ToEvent(Coupon coupon) => new()
    {
        CouponId = coupon.Id,
        PromotionId = coupon.PromotionId,
        CodeDigest = coupon.CodeDigest,
        MaskedCode = coupon.MaskedCode,
        Status = coupon.Status.ToString(),
        ValidFromUtc = coupon.ValidFromUtc,
        ValidUntilUtc = coupon.ValidUntilUtc,
        AssignedUserId = coupon.AssignedUserId,
        TotalLimit = coupon.TotalLimit,
        PerUserLimit = coupon.PerUserLimit,
        QuotaStrategy = coupon.QuotaStrategy.ToString(),
        Revision = coupon.Revision,
        QuotaBuckets = coupon.QuotaBuckets
            .OrderBy(bucket => bucket.BucketNumber)
            .Select(bucket => new CouponQuotaBucketContract(bucket.BucketNumber, bucket.Capacity))
            .ToArray()
    };

    private static ProductVariantChangedIntegrationEvent ToVariantEvent(
        Product product,
        ProductVariant variant,
        bool isActive) => new()
    {
        ProductId = product.Id,
        VariantId = variant.Id,
        CategoryId = product.CategoryId,
        Sku = variant.NormalizedSku,
        ProductName = product.Name,
        VariantName = variant.DisplayName,
        AttributesJson = variant.AttributesJson,
        Price = variant.Price,
        Currency = variant.Currency,
        IsActive = isActive,
        IsDefault = variant.IsDefault,
        LifecycleStatus = product.LifecycleStatus.ToString(),
        Revision = product.Revision,
        EffectiveOnUtc = product.EffectiveOnUtc
    };
}
