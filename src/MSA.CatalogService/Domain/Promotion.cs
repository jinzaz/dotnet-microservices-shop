using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSA.CatalogService.Domain;

public sealed record PromotionRuleDefinition(
    string RuleType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence,
    Guid? Id = null);

public sealed record PromotionBenefitDefinition(
    string BenefitType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence,
    Guid? Id = null);

public sealed record PromotionTargetDefinition(
    string TargetType,
    string? TargetKey,
    bool IsExcluded,
    int SchemaVersion = 1,
    string ConfigurationJson = "{}",
    int Sequence = 0,
    Guid? Id = null);

public sealed class Promotion : RevisionedCatalogAggregateRoot<Guid>
{
    private readonly List<PromotionRule> _rules = [];
    private readonly List<PromotionBenefit> _benefits = [];
    private readonly List<PromotionTarget> _targets = [];

    private Promotion()
    {
    }

    public Promotion(
        Guid id,
        string code,
        string name,
        PromotionTriggerType triggerType,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int priority,
        PromotionStackPolicy stackPolicy,
        string? exclusiveGroup,
        string actor,
        DateTimeOffset createdOnUtc)
    {
        CatalogDomainGuard.ValidWindow(
            startsOnUtc,
            endsOnUtc,
            nameof(startsOnUtc),
            nameof(endsOnUtc));

        Id = CatalogDomainGuard.Required(id, nameof(id));
        Code = CatalogDomainGuard.Required(code, nameof(code), 60);
        NormalizedCode = NormalizeCode(Code);
        Name = CatalogDomainGuard.Required(name, nameof(name), 200);
        TriggerType = triggerType;
        StartsOnUtc = CatalogDomainGuard.Utc(startsOnUtc);
        EndsOnUtc = CatalogDomainGuard.Utc(endsOnUtc);
        Priority = ValidatePriority(priority);
        StackPolicy = stackPolicy;
        ExclusiveGroup = NormalizeExclusiveGroup(exclusiveGroup);
        Status = PromotionStatus.Draft;
        InitializeAudit(actor, createdOnUtc);
        InitializeRevision();
    }

    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PromotionTriggerType TriggerType { get; private set; }
    public PromotionStatus Status { get; private set; }
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int Priority { get; private set; }
    public PromotionStackPolicy StackPolicy { get; private set; }
    public string? ExclusiveGroup { get; private set; }
    public long? PublishedRevision { get; private set; }
    public DateTimeOffset? PublishedOnUtc { get; private set; }
    public string DefinitionChecksum { get; private set; } = string.Empty;
    public IReadOnlyCollection<PromotionRule> Rules => _rules;
    public IReadOnlyCollection<PromotionBenefit> Benefits => _benefits;
    public IReadOnlyCollection<PromotionTarget> Targets => _targets;

    public static string NormalizeCode(string code) =>
        CatalogDomainGuard.Required(code, nameof(code), 60).ToUpperInvariant();

    public bool IsEffectiveAt(DateTimeOffset instantUtc)
    {
        var utc = CatalogDomainGuard.Utc(instantUtc);
        return Status == PromotionStatus.Active
            && StartsOnUtc <= utc
            && utc < EndsOnUtc;
    }

    public void Rename(string name, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        var normalizedName = CatalogDomainGuard.Required(name, nameof(name), 200);
        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return;

        Name = normalizedName;
        TouchRevision(actor, occurredOnUtc);
    }

    public void Reschedule(
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        CatalogDomainGuard.ValidWindow(
            startsOnUtc,
            endsOnUtc,
            nameof(startsOnUtc),
            nameof(endsOnUtc));
        var normalizedStart = CatalogDomainGuard.Utc(startsOnUtc);
        var normalizedEnd = CatalogDomainGuard.Utc(endsOnUtc);
        if (StartsOnUtc == normalizedStart && EndsOnUtc == normalizedEnd)
            return;

        StartsOnUtc = normalizedStart;
        EndsOnUtc = normalizedEnd;
        TouchRevision(actor, occurredOnUtc);
    }

    public void ChangeExecutionPolicy(
        int priority,
        PromotionStackPolicy stackPolicy,
        string? exclusiveGroup,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        var normalizedPriority = ValidatePriority(priority);
        var normalizedGroup = NormalizeExclusiveGroup(exclusiveGroup);
        if (Priority == normalizedPriority
            && StackPolicy == stackPolicy
            && string.Equals(ExclusiveGroup, normalizedGroup, StringComparison.Ordinal))
        {
            return;
        }

        Priority = normalizedPriority;
        StackPolicy = stackPolicy;
        ExclusiveGroup = normalizedGroup;
        TouchRevision(actor, occurredOnUtc);
    }

    public void ReplaceDefinition(
        IEnumerable<PromotionRuleDefinition> rules,
        IEnumerable<PromotionBenefitDefinition> benefits,
        IEnumerable<PromotionTargetDefinition> targets,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(benefits);
        ArgumentNullException.ThrowIfNull(targets);
        EnsureDraft();

        var replacementRules = rules
            .Select(definition => new PromotionRule(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.RuleType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                definition.Sequence))
            .ToArray();
        var replacementBenefits = benefits
            .Select(definition => new PromotionBenefit(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.BenefitType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                definition.Sequence))
            .ToArray();
        var replacementTargets = targets
            .Select((definition, index) => new PromotionTarget(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.TargetType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                index,
                definition.TargetKey,
                definition.IsExcluded))
            .ToArray();

        ValidateDefinition(replacementRules, replacementBenefits, replacementTargets);
        _rules.Clear();
        _rules.AddRange(replacementRules);
        _benefits.Clear();
        _benefits.AddRange(replacementBenefits);
        _targets.Clear();
        _targets.AddRange(replacementTargets);
        TouchRevision(actor, occurredOnUtc);
    }

    /// <summary>
    /// Replaces metadata and all typed components as one business revision. This is the preferred
    /// operation for an admin edit screen because observers never see a partially revised definition.
    /// </summary>
    public void ReplaceDefinition(
        string name,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int priority,
        PromotionStackPolicy stackPolicy,
        string? exclusiveGroup,
        IEnumerable<PromotionRuleDefinition> rules,
        IEnumerable<PromotionBenefitDefinition> benefits,
        IEnumerable<PromotionTargetDefinition> targets,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(benefits);
        ArgumentNullException.ThrowIfNull(targets);
        EnsureDraft();
        CatalogDomainGuard.ValidWindow(
            startsOnUtc,
            endsOnUtc,
            nameof(startsOnUtc),
            nameof(endsOnUtc));

        var normalizedName = CatalogDomainGuard.Required(name, nameof(name), 200);
        var normalizedStart = CatalogDomainGuard.Utc(startsOnUtc);
        var normalizedEnd = CatalogDomainGuard.Utc(endsOnUtc);
        var normalizedPriority = ValidatePriority(priority);
        var normalizedGroup = NormalizeExclusiveGroup(exclusiveGroup);
        var replacementRules = rules
            .Select(definition => new PromotionRule(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.RuleType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                definition.Sequence))
            .ToArray();
        var replacementBenefits = benefits
            .Select(definition => new PromotionBenefit(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.BenefitType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                definition.Sequence))
            .ToArray();
        var replacementTargets = targets
            .Select((definition, index) => new PromotionTarget(
                definition.Id ?? Guid.NewGuid(),
                Id,
                definition.TargetType,
                definition.SchemaVersion,
                definition.ConfigurationJson,
                index,
                definition.TargetKey,
                definition.IsExcluded))
            .ToArray();
        ValidateDefinition(replacementRules, replacementBenefits, replacementTargets);

        Name = normalizedName;
        StartsOnUtc = normalizedStart;
        EndsOnUtc = normalizedEnd;
        Priority = normalizedPriority;
        StackPolicy = stackPolicy;
        ExclusiveGroup = normalizedGroup;
        _rules.Clear();
        _rules.AddRange(replacementRules);
        _benefits.Clear();
        _benefits.AddRange(replacementBenefits);
        _targets.Clear();
        _targets.AddRange(replacementTargets);
        TouchRevision(actor, occurredOnUtc);
    }

    public void ReplaceDefinition(
        IEnumerable<PromotionRule> rules,
        IEnumerable<PromotionBenefit> benefits,
        IEnumerable<PromotionTarget> targets,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(benefits);
        ArgumentNullException.ThrowIfNull(targets);
        EnsureDraft();

        var replacementRules = rules.ToArray();
        var replacementBenefits = benefits.ToArray();
        var replacementTargets = targets.ToArray();
        if (replacementRules.Any(item => item.PromotionId != Id)
            || replacementBenefits.Any(item => item.PromotionId != Id)
            || replacementTargets.Any(item => item.PromotionId != Id))
        {
            throw new InvalidOperationException("Every definition component must belong to this promotion.");
        }

        ValidateDefinition(replacementRules, replacementBenefits, replacementTargets);
        _rules.Clear();
        _rules.AddRange(replacementRules);
        _benefits.Clear();
        _benefits.AddRange(replacementBenefits);
        _targets.Clear();
        _targets.AddRange(replacementTargets);
        TouchRevision(actor, occurredOnUtc);
    }

    public PromotionRule AddRule(
        string ruleType,
        int schemaVersion,
        string configurationJson,
        int sequence,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        EnsureSequenceAvailable(_rules.Select(item => item.Sequence), sequence, "rule");
        var rule = new PromotionRule(
            Guid.NewGuid(), Id, ruleType, schemaVersion, configurationJson, sequence);
        _rules.Add(rule);
        TouchRevision(actor, occurredOnUtc);
        return rule;
    }

    public PromotionBenefit AddBenefit(
        string benefitType,
        int schemaVersion,
        string configurationJson,
        int sequence,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        EnsureSequenceAvailable(_benefits.Select(item => item.Sequence), sequence, "benefit");
        var benefit = new PromotionBenefit(
            Guid.NewGuid(), Id, benefitType, schemaVersion, configurationJson, sequence);
        _benefits.Add(benefit);
        TouchRevision(actor, occurredOnUtc);
        return benefit;
    }

    public PromotionTarget AddTarget(
        string targetType,
        string? targetKey,
        bool isExcluded,
        string actor,
        DateTimeOffset occurredOnUtc,
        int schemaVersion = 1,
        string configurationJson = "{}",
        int sequence = 0)
    {
        EnsureDraft();
        EnsureSequenceAvailable(_targets.Select(item => item.Sequence), sequence, "target");
        var target = new PromotionTarget(
            Guid.NewGuid(), Id, targetType, schemaVersion, configurationJson,
            sequence, targetKey, isExcluded);
        _targets.Add(target);
        TouchRevision(actor, occurredOnUtc);
        return target;
    }

    public void RemoveRule(Guid ruleId, string actor, DateTimeOffset occurredOnUtc) =>
        RemoveComponent(_rules, item => item.Id, ruleId, actor, occurredOnUtc, "rule");

    public void RemoveBenefit(Guid benefitId, string actor, DateTimeOffset occurredOnUtc) =>
        RemoveComponent(_benefits, item => item.Id, benefitId, actor, occurredOnUtc, "benefit");

    public void RemoveTarget(Guid targetId, string actor, DateTimeOffset occurredOnUtc) =>
        RemoveComponent(_targets, item => item.Id, targetId, actor, occurredOnUtc, "target");

    public void Publish(string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        if (utc >= EndsOnUtc)
            throw new InvalidOperationException("An expired promotion cannot be published.");
        if (_benefits.Count == 0)
            throw new InvalidOperationException("A promotion requires at least one benefit before it can be published.");

        ValidateDefinition(_rules, _benefits, _targets);
        // Ordering evaluates the validity window itself. Publishing must immediately
        // make the snapshot eligible; otherwise a scheduled row could remain inert
        // when no activation worker happens to run at StartsOnUtc.
        Status = PromotionStatus.Active;
        PublishedOnUtc = utc;
        TouchRevision(actor, utc);
        PublishedRevision = Revision;
        DefinitionChecksum = CalculateDefinitionChecksum();
        AddSnapshotChanged("Published");
    }

    public void Activate(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Active)
            return;
        if (Status != PromotionStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled promotion can be activated.");

        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        EnsureWithinWindow(utc);
        Status = PromotionStatus.Active;
        CompleteStatusChange(actor, utc, "Activated");
    }

    public void Pause(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Paused)
            return;
        if (Status != PromotionStatus.Active)
            throw new InvalidOperationException("Only an active promotion can be paused.");

        Status = PromotionStatus.Paused;
        CompleteStatusChange(actor, occurredOnUtc, "Paused");
    }

    public void Resume(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Active)
            return;
        if (Status != PromotionStatus.Paused)
            throw new InvalidOperationException("Only a paused promotion can be resumed.");

        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        EnsureWithinWindow(utc);
        Status = PromotionStatus.Active;
        CompleteStatusChange(actor, utc, "Resumed");
    }

    public void End(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Ended)
            return;
        if (Status is PromotionStatus.Draft or PromotionStatus.Archived)
            throw new InvalidOperationException("Only a published promotion can be ended.");

        Status = PromotionStatus.Ended;
        CompleteStatusChange(actor, occurredOnUtc, "Ended");
    }

    public void BeginDraftRevision(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Draft)
            return;
        if (Status is PromotionStatus.Active or PromotionStatus.Archived)
            throw new InvalidOperationException("An active or archived promotion cannot start a draft revision.");

        Status = PromotionStatus.Draft;
        PublishedRevision = null;
        PublishedOnUtc = null;
        DefinitionChecksum = string.Empty;
        CompleteStatusChange(actor, occurredOnUtc, "DraftRevisionStarted");
    }

    public void Archive(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == PromotionStatus.Archived)
            return;
        if (Status is PromotionStatus.Active or PromotionStatus.Scheduled or PromotionStatus.Paused)
            throw new InvalidOperationException("A live promotion must be ended before it can be archived.");

        Status = PromotionStatus.Archived;
        CompleteStatusChange(actor, occurredOnUtc, "Archived");
    }

    private static int ValidatePriority(int priority)
    {
        if (priority is < -1_000_000 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(priority));
        return priority;
    }

    private static string? NormalizeExclusiveGroup(string? exclusiveGroup) =>
        CatalogDomainGuard.OptionalOrNull(exclusiveGroup, nameof(exclusiveGroup), 80)?.ToUpperInvariant();

    private void EnsureDraft()
    {
        if (Status != PromotionStatus.Draft)
            throw new InvalidOperationException("Published promotion definitions are immutable; start a new draft revision first.");
    }

    private void EnsureWithinWindow(DateTimeOffset utc)
    {
        if (utc < StartsOnUtc)
            throw new InvalidOperationException("The promotion start time has not been reached.");
        if (utc >= EndsOnUtc)
            throw new InvalidOperationException("The promotion validity window has ended.");
    }

    private static void ValidateDefinition(
        IReadOnlyCollection<PromotionRule> rules,
        IReadOnlyCollection<PromotionBenefit> benefits,
        IReadOnlyCollection<PromotionTarget> targets)
    {
        EnsureUniqueIdsAndSequences(rules.Select(item => (item.Id, item.Sequence)), "rules");
        EnsureUniqueIdsAndSequences(benefits.Select(item => (item.Id, item.Sequence)), "benefits");
        EnsureUniqueIdsAndSequences(targets.Select(item => (item.Id, item.Sequence)), "targets");
    }

    private static void EnsureUniqueIdsAndSequences(
        IEnumerable<(Guid Id, int Sequence)> components,
        string componentName)
    {
        var materialized = components.ToArray();
        if (materialized.Select(item => item.Id).Distinct().Count() != materialized.Length)
            throw new InvalidOperationException($"Promotion {componentName} must have unique identifiers.");
        if (materialized.Select(item => item.Sequence).Distinct().Count() != materialized.Length)
            throw new InvalidOperationException($"Promotion {componentName} must have unique sequence numbers.");
    }

    private static void EnsureSequenceAvailable(
        IEnumerable<int> existingSequences,
        int sequence,
        string componentName)
    {
        if (existingSequences.Contains(sequence))
            throw new InvalidOperationException($"A promotion {componentName} already uses sequence {sequence}.");
    }

    private void RemoveComponent<T>(
        List<T> components,
        Func<T, Guid> idSelector,
        Guid componentId,
        string actor,
        DateTimeOffset occurredOnUtc,
        string componentName)
    {
        EnsureDraft();
        CatalogDomainGuard.Required(componentId, nameof(componentId));
        var component = components.SingleOrDefault(item => idSelector(item) == componentId)
            ?? throw new KeyNotFoundException($"Promotion {componentName} was not found.");
        components.Remove(component);
        TouchRevision(actor, occurredOnUtc);
    }

    private string CalculateDefinitionChecksum()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Code = NormalizedCode,
            TriggerType = (int)TriggerType,
            StartsOnUtc,
            EndsOnUtc,
            Priority,
            StackPolicy = (int)StackPolicy,
            ExclusiveGroup,
            Rules = _rules.OrderBy(item => item.Sequence).Select(item => new
            {
                item.RuleType,
                item.SchemaVersion,
                item.ConfigurationJson,
                item.Sequence
            }),
            Benefits = _benefits.OrderBy(item => item.Sequence).Select(item => new
            {
                item.BenefitType,
                item.SchemaVersion,
                item.ConfigurationJson,
                item.Sequence
            }),
            Targets = _targets.OrderBy(item => item.Sequence).Select(item => new
            {
                item.TargetType,
                item.SchemaVersion,
                item.ConfigurationJson,
                item.Sequence,
                item.TargetKey,
                item.IsExcluded
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private void CompleteStatusChange(string actor, DateTimeOffset occurredOnUtc, string change)
    {
        TouchRevision(actor, occurredOnUtc);
        AddSnapshotChanged(change);
    }

    private void AddSnapshotChanged(string change) =>
        AddDomainEvent(new PromotionSnapshotChangedDomainEvent(this, Revision, change));
}

public interface IPromotionComponent
{
    Guid Id { get; }
    Guid PromotionId { get; }
    int SchemaVersion { get; }
    string ConfigurationJson { get; }
    int Sequence { get; }
}

public sealed class PromotionRule : IPromotionComponent
{
    private PromotionRule()
    {
    }

    public PromotionRule(
        Guid id,
        Guid promotionId,
        string ruleType,
        int schemaVersion,
        string configurationJson,
        int sequence)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        PromotionId = CatalogDomainGuard.Required(promotionId, nameof(promotionId));
        PromotionComponentGuard.Validate(schemaVersion, sequence);
        RuleType = CatalogDomainGuard.Required(ruleType, nameof(ruleType), 80);
        SchemaVersion = schemaVersion;
        ConfigurationJson = PromotionJson.CanonicalObject(configurationJson);
        Sequence = sequence;
        PromotionJson.ValidateRule(RuleType, SchemaVersion, ConfigurationJson);
    }

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public string RuleType { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string ConfigurationJson { get; private set; } = "{}";
    public int Sequence { get; private set; }
}

public sealed class PromotionBenefit : IPromotionComponent
{
    private PromotionBenefit()
    {
    }

    public PromotionBenefit(
        Guid id,
        Guid promotionId,
        string benefitType,
        int schemaVersion,
        string configurationJson,
        int sequence)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        PromotionId = CatalogDomainGuard.Required(promotionId, nameof(promotionId));
        PromotionComponentGuard.Validate(schemaVersion, sequence);
        BenefitType = CatalogDomainGuard.Required(benefitType, nameof(benefitType), 80);
        SchemaVersion = schemaVersion;
        ConfigurationJson = PromotionJson.CanonicalObject(configurationJson);
        Sequence = sequence;
        PromotionJson.ValidateBenefit(BenefitType, SchemaVersion, ConfigurationJson);
    }

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public string BenefitType { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string ConfigurationJson { get; private set; } = "{}";
    public int Sequence { get; private set; }
}

public sealed class PromotionTarget : IPromotionComponent
{
    private PromotionTarget()
    {
    }

    public PromotionTarget(
        Guid id,
        Guid promotionId,
        string targetType,
        string? targetKey,
        bool isExcluded)
        : this(id, promotionId, targetType, 1, "{}", 0, targetKey, isExcluded)
    {
    }

    public PromotionTarget(
        Guid id,
        Guid promotionId,
        string targetType,
        int schemaVersion,
        string configurationJson,
        int sequence,
        string? targetKey,
        bool isExcluded)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        PromotionId = CatalogDomainGuard.Required(promotionId, nameof(promotionId));
        PromotionComponentGuard.Validate(schemaVersion, sequence);
        TargetType = CatalogDomainGuard.Required(targetType, nameof(targetType), 40);
        SchemaVersion = schemaVersion;
        ConfigurationJson = PromotionJson.CanonicalObject(configurationJson);
        Sequence = sequence;
        TargetKey = CatalogDomainGuard.OptionalOrNull(targetKey, nameof(targetKey), 200);
        IsExcluded = isExcluded;
        PromotionJson.ValidateTarget(TargetType, TargetKey);
    }

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public string TargetType { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; }
    public string ConfigurationJson { get; private set; } = "{}";
    public int Sequence { get; private set; }
    public string? TargetKey { get; private set; }
    public bool IsExcluded { get; private set; }
}

internal static class PromotionComponentGuard
{
    public static void Validate(int schemaVersion, int sequence)
    {
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be positive.");
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence cannot be negative.");
    }
}

internal static class PromotionJson
{
    private const int MaximumConfigurationLength = 32_768;

    public static string CanonicalObject(string configurationJson)
    {
        var value = CatalogDomainGuard.Required(
            configurationJson,
            nameof(configurationJson),
            MaximumConfigurationLength);
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Promotion configuration must be a JSON object.", nameof(configurationJson));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonical(document.RootElement, writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Promotion configuration is not valid JSON.", nameof(configurationJson), exception);
        }
    }

    public static void ValidateRule(string ruleType, int schemaVersion, string configurationJson)
    {
        if (schemaVersion != 1)
            return;

        if (string.Equals(ruleType, "MinimumOrderAmount", StringComparison.OrdinalIgnoreCase))
        {
            EnsurePositiveDecimal(configurationJson, "amount", "MinimumOrderAmount.amount");
        }
        else if (string.Equals(ruleType, "MinimumItemQuantity", StringComparison.OrdinalIgnoreCase))
        {
            EnsurePositiveInt32(configurationJson, "quantity", "MinimumItemQuantity.quantity");
        }
    }

    public static void ValidateBenefit(string benefitType, int schemaVersion, string configurationJson)
    {
        if (schemaVersion != 1)
            return;

        if (string.Equals(benefitType, "PercentageOff", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(configurationJson);
            var percentage = RequiredDecimal(document.RootElement, "percentage", "PercentageOff.percentage");
            if (percentage is <= 0 or > 100)
                throw new ArgumentException("PercentageOff.percentage must be in (0, 100].", nameof(configurationJson));
            if (document.RootElement.TryGetProperty("maximumDiscount", out var maximum)
                && (!maximum.TryGetDecimal(out var value) || value <= 0))
            {
                throw new ArgumentException("PercentageOff.maximumDiscount must be positive.", nameof(configurationJson));
            }
        }
        else if (string.Equals(benefitType, "FixedAmountOff", StringComparison.OrdinalIgnoreCase))
        {
            EnsurePositiveDecimal(configurationJson, "amount", "FixedAmountOff.amount");
        }
    }

    public static void ValidateTarget(string targetType, string? targetKey)
    {
        if (string.Equals(targetType, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (targetKey is not null)
                throw new ArgumentException("The All promotion target cannot have a target key.", nameof(targetKey));
            return;
        }

        if (string.Equals(targetType, "Sku", StringComparison.OrdinalIgnoreCase))
        {
            if (targetKey is null)
                throw new ArgumentException("A SKU promotion target requires a target key.", nameof(targetKey));
            return;
        }

        if (string.Equals(targetType, "Product", StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetType, "Category", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(targetKey, out var id) || id == Guid.Empty)
                throw new ArgumentException($"A {targetType} promotion target requires a GUID target key.", nameof(targetKey));
        }
    }

    private static void EnsurePositiveDecimal(
        string configurationJson,
        string propertyName,
        string displayName)
    {
        using var document = JsonDocument.Parse(configurationJson);
        var value = RequiredDecimal(document.RootElement, propertyName, displayName);
        if (value <= 0)
            throw new ArgumentException($"{displayName} must be positive.", nameof(configurationJson));
    }

    private static decimal RequiredDecimal(JsonElement root, string propertyName, string displayName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || !property.TryGetDecimal(out var value))
        {
            throw new ArgumentException($"{displayName} must be a decimal number.", "configurationJson");
        }
        return value;
    }

    private static void EnsurePositiveInt32(
        string configurationJson,
        string propertyName,
        string displayName)
    {
        using var document = JsonDocument.Parse(configurationJson);
        if (!document.RootElement.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value)
            || value <= 0)
        {
            throw new ArgumentException($"{displayName} must be a positive integer.", nameof(configurationJson));
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
