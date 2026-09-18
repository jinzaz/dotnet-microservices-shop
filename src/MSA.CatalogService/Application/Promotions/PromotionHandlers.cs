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

        var triggerType = PromotionDefinitionMapper.ParseEnum<PromotionTriggerType>(request.TriggerType, nameof(request.TriggerType));
        var stackPolicy = PromotionDefinitionMapper.ParseEnum<PromotionStackPolicy>(request.StackPolicy, nameof(request.StackPolicy));
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
        promotion.ReplaceDefinition(
            PromotionDefinitionMapper.ToRules(request.Rules),
            PromotionDefinitionMapper.ToBenefits(request.Benefits),
            PromotionDefinitionMapper.ToTargets(request.Targets),
            request.Actor,
            now);
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
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        var stackPolicy = PromotionDefinitionMapper.ParseEnum<PromotionStackPolicy>(
            request.StackPolicy,
            nameof(request.StackPolicy));

        promotion.ReplaceDefinition(
            request.Name,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.Priority,
            stackPolicy,
            request.ExclusiveGroup,
            PromotionDefinitionMapper.ToRules(request.Rules),
            PromotionDefinitionMapper.ToBenefits(request.Benefits),
            PromotionDefinitionMapper.ToTargets(request.Targets),
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
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
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
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.Pause(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Paused", request.Actor, now);
        return true;
    }
}

internal sealed class RevisePromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<RevisePromotionCommand, bool>
{
    public async Task<bool> Handle(RevisePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.BeginDraftRevision(request.Actor, now);
        // The draft snapshot tells Ordering to stop applying the old definition immediately.
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "DraftRevisionStarted", request.Actor, now);
        return true;
    }
}

internal sealed class ArchivePromotionHandler(
    IPromotionRepository promotions,
    ICatalogSnapshotPublisher snapshots,
    ICatalogAuditWriter audit,
    TimeProvider clock) : IRequestHandler<ArchivePromotionCommand, bool>
{
    public async Task<bool> Handle(ArchivePromotionCommand request, CancellationToken cancellationToken)
    {
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
        var now = clock.GetUtcNow();
        promotion.Archive(request.Actor, now);
        snapshots.Publish(promotion);
        audit.Record("Promotion", promotion.Id, "Archived", request.Actor, now);
        return true;
    }
}

internal sealed class ListStorefrontPromotionsHandler(ICatalogReadService reads, TimeProvider clock)
    : IRequestHandler<ListStorefrontPromotionsQuery, IReadOnlyList<StorefrontPromotionDto>>
{
    public async Task<IReadOnlyList<StorefrontPromotionDto>> Handle(
        ListStorefrontPromotionsQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await reads.ListPromotionsAsync(0, 200, nameof(PromotionStatus.Active), cancellationToken);
        return active.Items
            .Where(promotion =>
                string.Equals(promotion.TriggerType, nameof(PromotionTriggerType.Automatic), StringComparison.Ordinal)
                && promotion.StartsOnUtc <= now
                && now < promotion.EndsOnUtc)
            .OrderByDescending(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.EndsOnUtc)
            .Select(promotion => new StorefrontPromotionDto(
                promotion.Id,
                promotion.Name,
                promotion.StartsOnUtc,
                promotion.EndsOnUtc,
                promotion.Priority,
                promotion.StackPolicy,
                promotion.Rules.Select(rule => new StorefrontPromotionRuleDto(rule.RuleType, rule.SchemaVersion, rule.ConfigurationJson)).ToArray(),
                promotion.Benefits.Select(benefit => new StorefrontPromotionBenefitDto(benefit.BenefitType, benefit.SchemaVersion, benefit.ConfigurationJson)).ToArray(),
                promotion.Targets.Select(target => new StorefrontPromotionTargetDto(target.TargetType, target.TargetKey, target.IsExcluded)).ToArray()))
            .ToArray();
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
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
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
        var promotion = await promotions.GetRequiredAsync(request.PromotionId, cancellationToken);
        promotion.EnsureVersion(request.ExpectedVersion);
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

/// <summary>Maps admin promotion input to domain definitions; shared by create and replace so both apply identical canonicalization.</summary>
internal static class PromotionDefinitionMapper
{
    public static IEnumerable<PromotionRuleDefinition> ToRules(IEnumerable<PromotionRuleInput> rules) =>
        rules.Select(rule => new PromotionRuleDefinition(
            rule.RuleType,
            rule.SchemaVersion,
            rule.ConfigurationJson,
            rule.Sequence));

    public static IEnumerable<PromotionBenefitDefinition> ToBenefits(IEnumerable<PromotionBenefitInput> benefits) =>
        benefits.Select(benefit => new PromotionBenefitDefinition(
            benefit.BenefitType,
            benefit.SchemaVersion,
            benefit.ConfigurationJson,
            benefit.Sequence));

    public static IEnumerable<PromotionTargetDefinition> ToTargets(IEnumerable<PromotionTargetInput> targets) =>
        targets.Select((target, sequence) => new PromotionTargetDefinition(
            CanonicalTargetType(target.TargetType),
            target.TargetKey,
            target.IsExcluded,
            Sequence: sequence));

    public static T ParseEnum<T>(string value, string parameterName)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unsupported {parameterName} '{value}'.", parameterName);

    public static string CanonicalTargetType(string targetType) =>
        targetType.ToUpperInvariant() switch
        {
            "ALL" => "All",
            "SKU" => "Sku",
            "PRODUCT" => "Product",
            "CATEGORY" => "Category",
            _ => throw new ArgumentException($"Unsupported target type '{targetType}'.", nameof(targetType))
        };
}
