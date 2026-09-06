using MSA.Infrastrucute.Entities;

namespace MSA.CatalogService.Domain;

public enum CategoryLifecycleStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

public enum ProductLifecycleStatus
{
    Draft = 0,
    Published = 1,
    Unpublished = 2,
    Archived = 3
}

public enum PromotionTriggerType
{
    Automatic = 0,
    Coupon = 1
}

public enum PromotionStatus
{
    Draft = 0,
    Scheduled = 1,
    Active = 2,
    Paused = 3,
    Ended = 4,
    Archived = 5
}

public enum PromotionStackPolicy
{
    Stackable = 0,
    Exclusive = 1
}

public enum CouponStatus
{
    Draft = 0,
    Active = 1,
    Disabled = 2,
    Expired = 3
}

public enum CouponQuotaStrategy
{
    SingleRow = 0,
    Bucketed = 1
}

/// <summary>
/// 目录聚合共用的审计与乐观并发状态。操作者和时间由调用方传入，保证领域变更可复现、可测试。
/// </summary>
public abstract class AuditedCatalogAggregateRoot<TId> : AggregateRoot<TId>
    where TId : notnull
{
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public string UpdatedBy { get; private set; } = string.Empty;
    public long Version { get; private set; }

    protected void InitializeAudit(string actor, DateTimeOffset occurredOnUtc)
    {
        var normalizedActor = CatalogDomainGuard.Required(actor, nameof(actor), 200);
        var utc = CatalogDomainGuard.Utc(occurredOnUtc);

        CreatedBy = normalizedActor;
        UpdatedBy = normalizedActor;
        CreatedOnUtc = utc;
        UpdatedOnUtc = utc;
        Version = 1;
    }

    protected void Touch(string actor, DateTimeOffset occurredOnUtc)
    {
        var normalizedActor = CatalogDomainGuard.Required(actor, nameof(actor), 200);
        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        if (utc < UpdatedOnUtc)
            throw new ArgumentOutOfRangeException(nameof(occurredOnUtc), "A change cannot predate the previous aggregate change.");

        // 聚合版本只随有效业务变更递增，是后台编辑冲突检测的统一依据。
        UpdatedBy = normalizedActor;
        UpdatedOnUtc = utc;
        checked
        {
            Version++;
        }
    }
}

public abstract class RevisionedCatalogAggregateRoot<TId> : AuditedCatalogAggregateRoot<TId>
    where TId : notnull
{
    public long Revision { get; private set; }

    protected void InitializeRevision() => Revision = 1;

    protected void TouchRevision(string actor, DateTimeOffset occurredOnUtc)
    {
        Touch(actor, occurredOnUtc);
        checked
        {
            Revision++;
        }
    }
}

internal static class CatalogDomainGuard
{
    public const string LegacyActor = "legacy-api";

    public static Guid Required(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        return value;
    }

    public static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        return normalized;
    }

    public static string Optional(string? value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        return normalized;
    }

    public static string? OptionalOrNull(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Required(value, parameterName, maximumLength);
    }

    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static void ValidWindow(
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        string startsParameterName,
        string endsParameterName)
    {
        if (Utc(startsOnUtc) >= Utc(endsOnUtc))
            throw new ArgumentException(
                $"{endsParameterName} must be later than {startsParameterName}.",
                endsParameterName);
    }
}
