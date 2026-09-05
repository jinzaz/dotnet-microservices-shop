using System.Text.Json;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Infrastructure;

internal sealed class CatalogAuditWriter(CatalogDbContext db) : ICatalogAuditWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string aggregateType,
        Guid aggregateId,
        string operation,
        string actor,
        DateTimeOffset occurredOnUtc,
        object? details = null)
    {
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details, SerializerOptions);
        db.CatalogAuditEntries.Add(new CatalogAuditEntry(
            Guid.NewGuid(),
            aggregateType,
            aggregateId,
            operation,
            actor,
            occurredOnUtc,
            detailsJson));
    }
}
