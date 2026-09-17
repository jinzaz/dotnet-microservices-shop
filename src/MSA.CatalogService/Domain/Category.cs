namespace MSA.CatalogService.Domain;

public sealed class Category : AuditedCatalogAggregateRoot<Guid>
{
    private Category()
    {
    }

    public Category(
        Guid id,
        string code,
        string name,
        string actor,
        DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        Code = CatalogDomainGuard.Required(code, nameof(code), 60);
        NormalizedCode = NormalizeCode(Code);
        Name = CatalogDomainGuard.Required(name, nameof(name), 120);
        Status = CategoryLifecycleStatus.Draft;
        InitializeAudit(actor, createdOnUtc);
        AddDomainEvent(new CategoryChangedDomainEvent(this, Version, "Created"));
    }

    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public CategoryLifecycleStatus Status { get; private set; }

    public static string NormalizeCode(string code) =>
        CatalogDomainGuard.Required(code, nameof(code), 60).ToUpperInvariant();

    public void Rename(string name, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureNotArchived();
        var normalizedName = CatalogDomainGuard.Required(name, nameof(name), 120);
        if (string.Equals(Name, normalizedName, StringComparison.Ordinal))
            return;

        Name = normalizedName;
        Touch(actor, occurredOnUtc);
        AddDomainEvent(new CategoryChangedDomainEvent(this, Version, "Renamed"));
    }

    public void Activate(string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureNotArchived();
        if (Status == CategoryLifecycleStatus.Active)
            return;

        Status = CategoryLifecycleStatus.Active;
        Touch(actor, occurredOnUtc);
        AddDomainEvent(new CategoryChangedDomainEvent(this, Version, "Activated"));
    }

    public void Archive(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CategoryLifecycleStatus.Archived)
            return;

        Status = CategoryLifecycleStatus.Archived;
        Touch(actor, occurredOnUtc);
        AddDomainEvent(new CategoryChangedDomainEvent(this, Version, "Archived"));
    }

    private void EnsureNotArchived()
    {
        if (Status == CategoryLifecycleStatus.Archived)
            throw new InvalidOperationException("An archived category cannot be changed.");
    }
}
