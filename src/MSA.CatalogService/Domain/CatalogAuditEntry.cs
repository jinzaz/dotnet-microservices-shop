using MSA.Infrastrucute.Entities;

namespace MSA.CatalogService.Domain;

public sealed class CatalogAuditEntry : Entity<Guid>
{
    private CatalogAuditEntry()
    {
    }

    public CatalogAuditEntry(
        Guid id,
        string aggregateType,
        Guid aggregateId,
        string operation,
        string actor,
        DateTimeOffset occurredOnUtc,
        string? detailsJson)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Audit entry id is required.", nameof(id));
        if (aggregateId == Guid.Empty)
            throw new ArgumentException("Aggregate id is required.", nameof(aggregateId));

        Id = id;
        AggregateType = Required(aggregateType, nameof(aggregateType), 80);
        AggregateId = aggregateId;
        Operation = Required(operation, nameof(operation), 100);
        Actor = Required(actor, nameof(actor), 200);
        OccurredOnUtc = occurredOnUtc;
        DetailsJson = detailsJson;
    }

    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; private set; }
    public string? DetailsJson { get; private set; }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maximumLength} characters.");
        return normalized;
    }
}
