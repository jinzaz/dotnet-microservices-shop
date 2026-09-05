using MediatR;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Application;

internal sealed class CreatePromotionHandler(
    IPromotionRepository promotions,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<CreatePromotionCommand, Guid>
{
    public async Task<Guid> Handle(CreatePromotionCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = Promotion.NormalizeCode(request.Code);
        if (await promotions.CodeExistsAsync(normalizedCode, null, cancellationToken))
            throw new InvalidOperationException($"Promotion code '{request.Code}' already exists.");

        var triggerType = ParseEnum<PromotionTriggerType>(request.TriggerType, nameof(request.TriggerType));
        var stackPolicy = ParseEnum<PromotionStackPolicy>(request.StackPolicy, nameof(request.StackPolicy));
        var id = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var promotion = new Promotion(
            id,
            request.Code,
            request.Name,
            triggerType,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.Priority,
            stackPolicy,
            request.ExclusiveGroup,
            request.Actor,
            now);
        ReplaceDefinition(promotion, request.Rules, request.Benefits, request.Targets, request.Actor, now);
        promotions.Add(promotion);
        audit.Record("Promotion", id, "DraftCreated", request.Actor, now, new
        {
            request.Code,
            request.TriggerType,
            RuleCount = request.Rules.Count,
            BenefitCount = request.Benefits.Count,
            TargetCount = request.Targets.Count
        });
        return id;
    }

    internal static void ReplaceDefinition(
        Promotion promotion,
        IReadOnlyList<PromotionRuleInput> rules,
        IReadOnlyList<PromotionBenefitInput> benefits,
        IReadOnlyList<PromotionTargetInput> targets,
        string actor,
        DateTimeOffset now)
    {
        promotion.ReplaceDefinition(
            rules.Select(rule => new PromotionRuleDefinition(
                rule.RuleType,
                rule.SchemaVersion,
                rule.ConfigurationJson,
                rule.Sequence)),
            benefits.Select(benefit => new PromotionBenefitDefinition(
                benefit.BenefitType,
                benefit.SchemaVersion,
                benefit.ConfigurationJson,
                benefit.Sequence)),
            targets.Select((target, sequence) => new PromotionTargetDefinition(
                CanonicalTargetType(target.TargetType),
                target.TargetKey,
                target.IsExcluded,
                Sequence: sequence)),
            actor,
            now);
    }

    internal static T ParseEnum<T>(string value, string parameterName)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unsupported {parameterName} '{value}'.", parameterName);

    private static string CanonicalTargetType(string targetType) =>
        targetType.ToUpperInvariant() switch
        {
            "ALL" => "All",
            "SKU" => "Sku",
            "PRODUCT" => "Product",
            "CATEGORY" => "Category",
            _ => throw new ArgumentException($"Unsupported target type '{targetType}'.", nameof(targetType))
        };
}

internal sealed class ReplacePromotionDefinitionHandler(
    IPromotionRepository promotions,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ReplacePromotionDefinitionCommand, bool>
{
    public async Task<bool> Handle(
        ReplacePromotionDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var promotion = await PromotionHandlerHelpers.GetAsync(promotions, request.PromotionId, cancellationToken);
        PromotionHandlerHelpers.EnsureVersion(promotion.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        var stackPolicy = CreatePromotionHandler.ParseEnum<PromotionStackPolicy>(
            request.StackPolicy,
            nameof(request.StackPolicy));

        promotion.ReplaceDefinition(
            request.Name,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.Priority,
            stackPolicy,
            request.ExclusiveGroup,
            request.Rules.Select(rule => new PromotionRuleDefinition(
                rule.RuleType,
                rule.SchemaVersion,
                rule.ConfigurationJson,
                rule.Sequence)),
            request.Benefits.Select(benefit => new PromotionBenefitDefinition(
                benefit.BenefitType,
                benefit.SchemaVersion,
                benefit.ConfigurationJson,
                benefit.Sequence)),
            request.Targets.Select((target, sequence) => new PromotionTargetDefinition(
                target.TargetType.ToUpperInvariant() switch
                {
                    "ALL" => "All",
                    "SKU" => "Sku",
                    "PRODUCT" => "Product",
                    "CATEGORY" => "Category",
                    _ => throw new ArgumentException($"Unsupported target type '{target.TargetType}'.")
                },
                target.TargetKey,
                target.IsExcluded,
                Sequence: sequence)),
            request.Actor,
            now);
        audit.Record("Promotion", promotion.Id, "DraftDefinitionReplaced", request.Actor, now);
        return true;
    }
}

internal sealed class PublishPromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<PublishPromotionCommand, bool>
{
    public async Task<bool> Handle(PublishPromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await PromotionHandlerHelpers.GetAsync(promotions, request.PromotionId, cancellationToken);
        PromotionHandlerHelpers.EnsureVersion(promotion.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.Publish(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Published", request.Actor, now, new
        {
            promotion.Revision,
            promotion.DefinitionChecksum
        });
        return true;
    }
}

internal sealed class PausePromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<PausePromotionCommand, bool>
{
    public async Task<bool> Handle(PausePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await PromotionHandlerHelpers.GetAsync(promotions, request.PromotionId, cancellationToken);
        PromotionHandlerHelpers.EnsureVersion(promotion.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.Pause(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Paused", request.Actor, now);
        return true;
    }
}

internal sealed class ResumePromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ResumePromotionCommand, bool>
{
    public async Task<bool> Handle(ResumePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await PromotionHandlerHelpers.GetAsync(promotions, request.PromotionId, cancellationToken);
        PromotionHandlerHelpers.EnsureVersion(promotion.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.Resume(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Resumed", request.Actor, now);
        return true;
    }
}

internal sealed class EndPromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<EndPromotionCommand, bool>
{
    public async Task<bool> Handle(EndPromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await PromotionHandlerHelpers.GetAsync(promotions, request.PromotionId, cancellationToken);
        PromotionHandlerHelpers.EnsureVersion(promotion.Version, request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.End(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Ended", request.Actor, now);
        return true;
    }
}

internal sealed class GetPromotionHandler(ICatalogReadService reads)
    : IRequestHandler<GetPromotionQuery, PromotionDto?>
{
    public Task<PromotionDto?> Handle(GetPromotionQuery request, CancellationToken cancellationToken) =>
        reads.GetPromotionAsync(request.PromotionId, cancellationToken);
}

internal sealed class ListPromotionsHandler(ICatalogReadService reads)
    : IRequestHandler<ListPromotionsQuery, PagedResult<PromotionDto>>
{
    public Task<PagedResult<PromotionDto>> Handle(
        ListPromotionsQuery request,
        CancellationToken cancellationToken) =>
        reads.ListPromotionsAsync(request.Skip, request.Take, request.Status, cancellationToken);
}

file static class PromotionHandlerHelpers
{
    public static async Task<Promotion> GetAsync(
        IPromotionRepository promotions,
        Guid promotionId,
        CancellationToken cancellationToken) =>
        await promotions.FindAsync(promotionId, cancellationToken)
        ?? throw new KeyNotFoundException("Promotion was not found.");

    public static void EnsureVersion(long current, long? expected)
    {
        if (expected.HasValue && expected.Value != current)
            throw new InvalidOperationException($"Promotion version mismatch. Expected {expected}, current {current}.");
    }
}
