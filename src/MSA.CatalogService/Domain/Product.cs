namespace MSA.CatalogService.Domain;

public sealed class Product : RevisionedCatalogAggregateRoot<Guid>
{
    private Product()
    {
    }

    /// <summary>
    /// Compatibility constructor for the original API. New products intentionally remain in Draft.
    /// </summary>
    public Product(
        Guid id,
        Guid categoryId,
        string sku,
        string name,
        string description,
        decimal price,
        string currency)
        : this(
            id,
            categoryId,
            sku,
            name,
            description,
            price,
            currency,
            CatalogDomainGuard.LegacyActor,
            DateTimeOffset.UtcNow)
    {
    }

    public Product(
        Guid id,
        Guid categoryId,
        string sku,
        string name,
        string description,
        decimal price,
        string currency,
        string actor,
        DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        CategoryId = CatalogDomainGuard.Required(categoryId, nameof(categoryId));
        Sku = CatalogDomainGuard.Required(sku, nameof(sku), 80);
        NormalizedSku = NormalizeSku(Sku);
        Name = CatalogDomainGuard.Required(name, nameof(name), 200);
        Description = CatalogDomainGuard.Optional(description, nameof(description), 4_000);
        Price = ValidatePrice(price);
        Currency = NormalizeCurrency(currency);
        LifecycleStatus = ProductLifecycleStatus.Draft;
        IsActive = false;
        InitializeAudit(actor, createdOnUtc);
        InitializeRevision();
        AddSnapshotChanged("Created");
    }

    public Guid CategoryId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string NormalizedSku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public ProductLifecycleStatus LifecycleStatus { get; private set; }

    /// <summary>
    /// Persisted lifecycle projection retained for efficient public-catalog queries and rolling compatibility.
    /// The aggregate is the only writer and always keeps it in sync with LifecycleStatus.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset? EffectiveOnUtc { get; private set; }
    public DateTimeOffset? PublishedOnUtc { get; private set; }
    public DateTimeOffset? UnpublishedOnUtc { get; private set; }
    public DateTimeOffset? ArchivedOnUtc { get; private set; }

    public static string NormalizeSku(string sku) =>
        CatalogDomainGuard.Required(sku, nameof(sku), 80).ToUpperInvariant();

    public bool IsSellableAt(DateTimeOffset instantUtc) =>
        LifecycleStatus == ProductLifecycleStatus.Published
        && IsActive
        && EffectiveOnUtc is not null
        && EffectiveOnUtc <= CatalogDomainGuard.Utc(instantUtc);

    public void UpdateDetails(
        string name,
        string description,
        string actor,
        DateTimeOffset occurredOnUtc) =>
        UpdateDetails(CategoryId, name, description, actor, occurredOnUtc);

    public void UpdateDetails(
        Guid categoryId,
        string name,
        string description,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var normalizedCategoryId = CatalogDomainGuard.Required(categoryId, nameof(categoryId));
        var normalizedName = CatalogDomainGuard.Required(name, nameof(name), 200);
        var normalizedDescription = CatalogDomainGuard.Optional(description, nameof(description), 4_000);

        if (CategoryId == normalizedCategoryId
            && string.Equals(Name, normalizedName, StringComparison.Ordinal)
            && string.Equals(Description, normalizedDescription, StringComparison.Ordinal))
        {
            return;
        }

        CategoryId = normalizedCategoryId;
        Name = normalizedName;
        Description = normalizedDescription;
        CompleteChange(actor, occurredOnUtc, "DetailsUpdated");
    }

    public void MoveToCategory(Guid categoryId, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var normalizedCategoryId = CatalogDomainGuard.Required(categoryId, nameof(categoryId));
        if (CategoryId == normalizedCategoryId)
            return;

        CategoryId = normalizedCategoryId;
        CompleteChange(actor, occurredOnUtc, "CategoryChanged");
    }

    public void ChangePrice(decimal price, string actor, DateTimeOffset occurredOnUtc) =>
        ChangePrice(price, Currency, actor, occurredOnUtc);

    public void ChangePrice(
        decimal price,
        string currency,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var normalizedPrice = ValidatePrice(price);
        var normalizedCurrency = NormalizeCurrency(currency);
        if (Price == normalizedPrice
            && string.Equals(Currency, normalizedCurrency, StringComparison.Ordinal))
        {
            return;
        }

        Price = normalizedPrice;
        Currency = normalizedCurrency;
        CompleteChange(actor, occurredOnUtc, "PriceChanged");
    }

    public void Publish(
        DateTimeOffset? effectiveOnUtc,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var occurredUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        var effectiveUtc = CatalogDomainGuard.Utc(effectiveOnUtc ?? occurredUtc);
        if (LifecycleStatus == ProductLifecycleStatus.Published
            && EffectiveOnUtc == effectiveUtc)
        {
            return;
        }

        LifecycleStatus = ProductLifecycleStatus.Published;
        IsActive = true;
        EffectiveOnUtc = effectiveUtc;
        PublishedOnUtc = occurredUtc;
        UnpublishedOnUtc = null;
        CompleteChange(actor, occurredUtc, "Published");
    }

    public void Publish(string actor, DateTimeOffset occurredOnUtc) =>
        Publish(occurredOnUtc, actor, occurredOnUtc);

    public void Unpublish(string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        if (LifecycleStatus is ProductLifecycleStatus.Draft or ProductLifecycleStatus.Unpublished)
            return;

        LifecycleStatus = ProductLifecycleStatus.Unpublished;
        IsActive = false;
        UnpublishedOnUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        CompleteChange(actor, occurredOnUtc, "Unpublished");
    }

    public void Archive(string actor, DateTimeOffset occurredOnUtc)
    {
        if (LifecycleStatus == ProductLifecycleStatus.Archived)
            return;
        if (LifecycleStatus == ProductLifecycleStatus.Published)
            throw new InvalidOperationException("A published product must be unpublished before it can be archived.");

        LifecycleStatus = ProductLifecycleStatus.Archived;
        IsActive = false;
        ArchivedOnUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        CompleteChange(actor, occurredOnUtc, "Archived");
    }

    /// <summary>Compatibility mutation for the original PUT endpoint.</summary>
    public void Update(string name, string description, decimal price, bool active)
    {
        EnsureEditable();
        var normalizedName = CatalogDomainGuard.Required(name, nameof(name), 200);
        var normalizedDescription = CatalogDomainGuard.Optional(description, nameof(description), 4_000);
        var normalizedPrice = ValidatePrice(price);
        var now = DateTimeOffset.UtcNow;
        var nextStatus = active
            ? ProductLifecycleStatus.Published
            : ProductLifecycleStatus.Unpublished;

        if (string.Equals(Name, normalizedName, StringComparison.Ordinal)
            && string.Equals(Description, normalizedDescription, StringComparison.Ordinal)
            && Price == normalizedPrice
            && LifecycleStatus == nextStatus)
        {
            return;
        }

        Name = normalizedName;
        Description = normalizedDescription;
        Price = normalizedPrice;
        LifecycleStatus = nextStatus;
        IsActive = active;
        if (active)
        {
            EffectiveOnUtc ??= now;
            PublishedOnUtc = now;
            UnpublishedOnUtc = null;
        }
        else
        {
            UnpublishedOnUtc = now;
        }

        CompleteChange(CatalogDomainGuard.LegacyActor, now, "LegacyUpdate");
    }

    private static decimal ValidatePrice(decimal price)
    {
        if (price is < 0 or > 9_999_999_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(price), "Price is outside the supported decimal(18,2) range.");
        return decimal.Round(price, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = CatalogDomainGuard.Required(currency, nameof(currency), 3).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(currency));
        return normalized;
    }

    private void EnsureEditable()
    {
        if (LifecycleStatus == ProductLifecycleStatus.Archived)
            throw new InvalidOperationException("An archived product cannot be changed.");
    }

    private void CompleteChange(string actor, DateTimeOffset occurredOnUtc, string change)
    {
        TouchRevision(actor, occurredOnUtc);
        AddSnapshotChanged(change);
    }

    private void AddSnapshotChanged(string change) =>
        AddDomainEvent(new ProductSnapshotChangedDomainEvent(this, Revision, change));
}
