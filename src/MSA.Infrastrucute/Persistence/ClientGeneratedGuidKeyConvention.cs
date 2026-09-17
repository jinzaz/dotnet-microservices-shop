using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MSA.Infrastrucute.Persistence;

/// <summary>
/// Aggregates in this solution assign their own Guid identities (Guid.NewGuid or upstream ids). EF Core otherwise
/// assumes a Guid key is generated on add, so a new child with a non-empty id reached through a tracked
/// aggregate's collection is treated as an existing row and saved with UPDATE, failing with
/// DbUpdateConcurrencyException ("expected to affect 1 row(s), but actually affected 0").
/// Explicit ValueGeneratedOnAdd mappings still win over this convention.
/// </summary>
public sealed class ClientGeneratedGuidKeyConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.FindPrimaryKey() is { Properties: [var keyProperty] }
                && keyProperty.ClrType == typeof(Guid))
            {
                keyProperty.Builder.ValueGenerated(ValueGenerated.Never);
            }
        }
    }
}
