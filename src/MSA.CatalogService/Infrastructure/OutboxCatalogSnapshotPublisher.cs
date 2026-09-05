using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using System.Text.Json;

namespace MSA.CatalogService.Infrastructure;

internal sealed class OutboxCatalogSnapshotPublisher(IOutboxStore outbox) : ICatalogSnapshotPublisher
{
    public void Publish(Product product) => PublishEvent(new ProductChangedIntegrationEvent
    {
        ProductId = product.Id,
        CategoryId = product.CategoryId,
        Sku = product.NormalizedSku,
        Name = product.Name,
        Price = product.Price,
        Currency = product.Currency,
        IsActive = product.IsActive,
        LifecycleStatus = product.LifecycleStatus.ToString(),
        Revision = product.Revision,
        EffectiveOnUtc = product.EffectiveOnUtc
    });

    public void Publish(Promotion promotion) => PublishEvent(new PromotionSnapshotChangedIntegrationEvent
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
    });

    public void Publish(Coupon coupon) => PublishEvent(new CouponSnapshotChangedIntegrationEvent
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
    });

    private void PublishEvent(IntegrationEvent integrationEvent) =>
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
}
