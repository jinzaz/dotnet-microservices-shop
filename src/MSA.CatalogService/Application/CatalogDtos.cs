namespace MSA.CatalogService.Application;

public sealed record ProductDto(
    Guid Id,
    Guid CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    bool IsActive,
    DateTimeOffset UpdatedOnUtc,
    string LifecycleStatus = "Published",
    string NormalizedSku = "",
    DateTimeOffset? EffectiveOnUtc = null,
    long Revision = 0,
    long Version = 0);

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    string Status,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    string CreatedBy,
    string UpdatedBy,
    long Version);

public sealed record PromotionRuleDto(
    Guid Id,
    string RuleType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence);

public sealed record PromotionBenefitDto(
    Guid Id,
    string BenefitType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence);

public sealed record PromotionTargetDto(
    Guid Id,
    string TargetType,
    string? TargetKey,
    bool IsExcluded);

public sealed record PromotionDto(
    Guid Id,
    string Code,
    string Name,
    string TriggerType,
    string Status,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int Priority,
    string StackPolicy,
    string? ExclusiveGroup,
    long Revision,
    string DefinitionChecksum,
    IReadOnlyList<PromotionRuleDto> Rules,
    IReadOnlyList<PromotionBenefitDto> Benefits,
    IReadOnlyList<PromotionTargetDto> Targets,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    string CreatedBy,
    string UpdatedBy,
    long Version);

public sealed record CouponQuotaBucketDto(int BucketNumber, int Capacity);

public sealed record CouponDto(
    Guid Id,
    Guid PromotionId,
    string MaskedCode,
    string Status,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string? AssignedUserId,
    int? TotalLimit,
    int PerUserLimit,
    string QuotaStrategy,
    long Revision,
    IReadOnlyList<CouponQuotaBucketDto> QuotaBuckets,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    string CreatedBy,
    string UpdatedBy,
    long Version);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Skip, int Take, int Total);
