namespace MSA.CatalogService.Domain;

public enum BrandLifecycleStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

/// <summary>
/// 品牌与分类同一套生命周期：草稿 → 启用 → 归档。只有启用的品牌能挂到商品上；
/// 商品的品牌是可选的（白牌、自营杂货没有品牌），所以品牌归档不影响已有商品继续销售，只是不能再新挂。
/// </summary>
public sealed class Brand : AuditedCatalogAggregateRoot<Guid>
{
    private Brand()
    {
    }

    public Brand(Guid id, string code, string name, string actor, DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        Code = CatalogDomainGuard.Required(code, nameof(code), 60);
        NormalizedCode = NormalizeCode(Code);
        Name = CatalogDomainGuard.Required(name, nameof(name), 120);
        Status = BrandLifecycleStatus.Draft;
        InitializeAudit(actor, createdOnUtc);
    }

    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public BrandLifecycleStatus Status { get; private set; }

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
    }

    public void Activate(string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureNotArchived();
        if (Status == BrandLifecycleStatus.Active)
            return;

        Status = BrandLifecycleStatus.Active;
        Touch(actor, occurredOnUtc);
    }

    public void Archive(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == BrandLifecycleStatus.Archived)
            return;

        Status = BrandLifecycleStatus.Archived;
        Touch(actor, occurredOnUtc);
    }

    private void EnsureNotArchived()
    {
        if (Status == BrandLifecycleStatus.Archived)
            throw new InvalidOperationException("An archived brand cannot be changed.");
    }
}
