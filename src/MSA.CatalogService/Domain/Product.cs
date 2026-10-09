using System.Text.Json;

namespace MSA.CatalogService.Domain;

public sealed class Product : RevisionedCatalogAggregateRoot<Guid>
{
    private readonly List<ProductVariant> _variants = [];
    private readonly List<ProductMedia> _media = [];

    private Product()
    {
    }

    /// <summary>
    /// The default variant deliberately reuses the product id so downstream product references created
    /// before variants existed remain valid.
    /// </summary>
    public Product(
        Guid id,
        Guid categoryId,
        string sku,
        string name,
        string description,
        decimal price,
        string currency,
        string actor,
        DateTimeOffset createdOnUtc,
        Guid? brandId = null)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        CategoryId = CatalogDomainGuard.Required(categoryId, nameof(categoryId));
        BrandId = OptionalBrand(brandId);
        Name = CatalogDomainGuard.Required(name, nameof(name), 200);
        Description = CatalogDomainGuard.Optional(description, nameof(description), 4_000);
        LifecycleStatus = ProductLifecycleStatus.Draft;
        IsActive = false;
        InitializeAudit(actor, createdOnUtc);
        InitializeRevision();
        _variants.Add(ProductVariant.CreateDefault(
            id,
            id,
            sku,
            name,
            price,
            currency,
            createdOnUtc));
        AddSnapshotChanged("Created");
    }

    public Guid CategoryId { get; private set; }

    /// <summary>可选：白牌、自营杂货等没有品牌。</summary>
    public Guid? BrandId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ProductLifecycleStatus LifecycleStatus { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? EffectiveOnUtc { get; private set; }
    public DateTimeOffset? PublishedOnUtc { get; private set; }
    public DateTimeOffset? UnpublishedOnUtc { get; private set; }
    public DateTimeOffset? ArchivedOnUtc { get; private set; }
    public IReadOnlyCollection<ProductVariant> Variants => _variants;
    public IReadOnlyCollection<ProductMedia> Media => _media;

    // Compatibility projection for callers that still treat a product as one SKU.
    public ProductVariant DefaultVariant =>
        _variants.SingleOrDefault(variant => variant.IsDefault)
        ?? throw new InvalidOperationException("Product does not have a default variant.");
    public string Sku => DefaultVariant.Sku;
    public string NormalizedSku => DefaultVariant.NormalizedSku;
    public decimal Price => DefaultVariant.Price;
    public string Currency => DefaultVariant.Currency;

    public static string NormalizeSku(string sku) => ProductVariant.NormalizeSku(sku);

    public bool IsSellableAt(DateTimeOffset instantUtc) =>
        LifecycleStatus == ProductLifecycleStatus.Published
        && IsActive
        && EffectiveOnUtc is not null
        && EffectiveOnUtc <= CatalogDomainGuard.Utc(instantUtc)
        && _variants.Any(variant => variant.IsActive);

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

    /// <summary>换品牌或去掉品牌（传 null）。品牌是否启用由应用层校验，这里只保证状态一致。</summary>
    public void ChangeBrand(Guid? brandId, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var normalizedBrandId = OptionalBrand(brandId);
        if (BrandId == normalizedBrandId)
            return;

        BrandId = normalizedBrandId;
        CompleteChange(actor, occurredOnUtc, "BrandChanged");
    }

    private static Guid? OptionalBrand(Guid? brandId) => brandId == Guid.Empty ? null : brandId;

    public ProductVariant AddVariant(
        Guid variantId,
        string sku,
        string displayName,
        string attributesJson,
        string? barcode,
        decimal price,
        string currency,
        bool isActive,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var variant = new ProductVariant(
            variantId,
            Id,
            sku,
            displayName,
            attributesJson,
            barcode,
            price,
            currency,
            isActive,
            isDefault: false,
            occurredOnUtc);
        if (!string.Equals(variant.Currency, DefaultVariant.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException("All variants of a product must use the same currency.");
        _variants.Add(variant);
        CompleteChange(actor, occurredOnUtc, "VariantAdded");
        return variant;
    }

    public void UpdateVariant(
        Guid variantId,
        string displayName,
        string attributesJson,
        string? barcode,
        decimal price,
        string currency,
        bool isActive,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var variant = GetVariant(variantId);
        var normalizedCurrency = CatalogDomainGuard.Required(currency, nameof(currency), 3).ToUpperInvariant();
        if (_variants.Any(item => item.Id != variantId
            && !string.Equals(item.Currency, normalizedCurrency, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("All variants of a product must use the same currency.");
        }
        if (!variant.Update(displayName, attributesJson, barcode, price, currency, isActive, occurredOnUtc))
            return;
        CompleteChange(actor, occurredOnUtc, "VariantUpdated");
    }

    public void ChangePrice(
        decimal price,
        string actor,
        DateTimeOffset occurredOnUtc) =>
        ChangePrice(price, DefaultVariant.Currency, actor, occurredOnUtc);

    public void ChangePrice(
        decimal price,
        string currency,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        if (!DefaultVariant.ChangePrice(price, currency, occurredOnUtc))
            return;
        CompleteChange(actor, occurredOnUtc, "PriceChanged");
    }

    public ProductVariant RemoveVariant(Guid variantId, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var variant = GetVariant(variantId);
        if (variant.IsDefault)
            throw new InvalidOperationException("The default variant cannot be removed.");
        _variants.Remove(variant);
        _media.RemoveAll(item => item.VariantId == variantId);
        CompleteChange(actor, occurredOnUtc, "VariantRemoved");
        return variant;
    }

    public ProductMedia AddMedia(
        Guid mediaId,
        Guid? variantId,
        ProductMediaType mediaType,
        string url,
        string? thumbnailUrl,
        string? altText,
        int sortOrder,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        EnsureVariantReference(variantId);
        var media = new ProductMedia(
            mediaId,
            Id,
            variantId,
            mediaType,
            url,
            thumbnailUrl,
            altText,
            sortOrder,
            occurredOnUtc);
        _media.Add(media);
        CompleteChange(actor, occurredOnUtc, "MediaAdded");
        return media;
    }

    public void UpdateMedia(
        Guid mediaId,
        Guid? variantId,
        ProductMediaType mediaType,
        string url,
        string? thumbnailUrl,
        string? altText,
        int sortOrder,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        EnsureVariantReference(variantId);
        var media = _media.SingleOrDefault(item => item.Id == mediaId)
            ?? throw new KeyNotFoundException("Product media was not found.");
        if (!media.Update(variantId, mediaType, url, thumbnailUrl, altText, sortOrder, occurredOnUtc))
            return;
        CompleteChange(actor, occurredOnUtc, "MediaUpdated");
    }

    public void RemoveMedia(Guid mediaId, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        var media = _media.SingleOrDefault(item => item.Id == mediaId)
            ?? throw new KeyNotFoundException("Product media was not found.");
        _media.Remove(media);
        CompleteChange(actor, occurredOnUtc, "MediaRemoved");
    }

    public void Publish(
        DateTimeOffset? effectiveOnUtc,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureEditable();
        if (!_variants.Any(variant => variant.IsActive))
            throw new InvalidOperationException("A product requires at least one active variant before publishing.");

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

    private ProductVariant GetVariant(Guid variantId) =>
        _variants.SingleOrDefault(variant => variant.Id == variantId)
        ?? throw new KeyNotFoundException("Product variant was not found.");

    private void EnsureVariantReference(Guid? variantId)
    {
        if (variantId.HasValue)
            _ = GetVariant(variantId.Value);
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

public sealed class ProductVariant
{
    private ProductVariant()
    {
    }

    internal ProductVariant(
        Guid id,
        Guid productId,
        string sku,
        string displayName,
        string attributesJson,
        string? barcode,
        decimal price,
        string currency,
        bool isActive,
        bool isDefault,
        DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        ProductId = CatalogDomainGuard.Required(productId, nameof(productId));
        Sku = CatalogDomainGuard.Required(sku, nameof(sku), 80);
        NormalizedSku = NormalizeSku(Sku);
        DisplayName = CatalogDomainGuard.Required(displayName, nameof(displayName), 200);
        AttributesJson = NormalizeAttributes(attributesJson);
        Barcode = CatalogDomainGuard.OptionalOrNull(barcode, nameof(barcode), 80);
        Price = ValidatePrice(price);
        Currency = NormalizeCurrency(currency);
        IsActive = isActive;
        IsDefault = isDefault;
        CreatedOnUtc = CatalogDomainGuard.Utc(createdOnUtc);
        UpdatedOnUtc = CreatedOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string NormalizedSku { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AttributesJson { get; private set; } = "{}";
    public string? Barcode { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "CNY";
    public bool IsActive { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    public static string NormalizeSku(string sku) =>
        CatalogDomainGuard.Required(sku, nameof(sku), 80).ToUpperInvariant();

    internal static ProductVariant CreateDefault(
        Guid id,
        Guid productId,
        string sku,
        string displayName,
        decimal price,
        string currency,
        DateTimeOffset createdOnUtc) =>
        new(
            id,
            productId,
            sku,
            displayName,
            "{}",
            null,
            price,
            currency,
            isActive: true,
            isDefault: true,
            createdOnUtc);

    internal bool Update(
        string displayName,
        string attributesJson,
        string? barcode,
        decimal price,
        string currency,
        bool isActive,
        DateTimeOffset occurredOnUtc)
    {
        var normalizedDisplayName = CatalogDomainGuard.Required(displayName, nameof(displayName), 200);
        var normalizedAttributes = NormalizeAttributes(attributesJson);
        var normalizedBarcode = CatalogDomainGuard.OptionalOrNull(barcode, nameof(barcode), 80);
        var normalizedPrice = ValidatePrice(price);
        var normalizedCurrency = NormalizeCurrency(currency);
        if (string.Equals(DisplayName, normalizedDisplayName, StringComparison.Ordinal)
            && string.Equals(AttributesJson, normalizedAttributes, StringComparison.Ordinal)
            && string.Equals(Barcode, normalizedBarcode, StringComparison.Ordinal)
            && Price == normalizedPrice
            && string.Equals(Currency, normalizedCurrency, StringComparison.Ordinal)
            && IsActive == isActive)
        {
            return false;
        }

        DisplayName = normalizedDisplayName;
        AttributesJson = normalizedAttributes;
        Barcode = normalizedBarcode;
        Price = normalizedPrice;
        Currency = normalizedCurrency;
        IsActive = isActive;
        UpdatedOnUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        return true;
    }

    internal bool ChangePrice(decimal price, string currency, DateTimeOffset occurredOnUtc)
    {
        var normalizedPrice = ValidatePrice(price);
        var normalizedCurrency = NormalizeCurrency(currency);
        if (Price == normalizedPrice && string.Equals(Currency, normalizedCurrency, StringComparison.Ordinal))
            return false;
        Price = normalizedPrice;
        Currency = normalizedCurrency;
        UpdatedOnUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        return true;
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

    private static string NormalizeAttributes(string attributesJson)
    {
        var value = string.IsNullOrWhiteSpace(attributesJson) ? "{}" : attributesJson.Trim();
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Variant attributes must be a JSON object.", nameof(attributesJson));
        return JsonSerializer.Serialize(document.RootElement);
    }
}

public sealed class ProductMedia
{
    private ProductMedia()
    {
    }

    internal ProductMedia(
        Guid id,
        Guid productId,
        Guid? variantId,
        ProductMediaType mediaType,
        string url,
        string? thumbnailUrl,
        string? altText,
        int sortOrder,
        DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        ProductId = CatalogDomainGuard.Required(productId, nameof(productId));
        Apply(variantId, mediaType, url, thumbnailUrl, altText, sortOrder);
        CreatedOnUtc = CatalogDomainGuard.Utc(createdOnUtc);
        UpdatedOnUtc = CreatedOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public ProductMediaType MediaType { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? ThumbnailUrl { get; private set; }
    public string? AltText { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    internal bool Update(
        Guid? variantId,
        ProductMediaType mediaType,
        string url,
        string? thumbnailUrl,
        string? altText,
        int sortOrder,
        DateTimeOffset occurredOnUtc)
    {
        var normalizedUrl = ValidateUrl(url, nameof(url));
        var normalizedThumbnail = string.IsNullOrWhiteSpace(thumbnailUrl)
            ? null
            : ValidateUrl(thumbnailUrl, nameof(thumbnailUrl));
        var normalizedAltText = CatalogDomainGuard.OptionalOrNull(altText, nameof(altText), 300);
        if (sortOrder is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        if (!Enum.IsDefined(mediaType))
            throw new ArgumentOutOfRangeException(nameof(mediaType));

        if (VariantId == variantId
            && MediaType == mediaType
            && string.Equals(Url, normalizedUrl, StringComparison.Ordinal)
            && string.Equals(ThumbnailUrl, normalizedThumbnail, StringComparison.Ordinal)
            && string.Equals(AltText, normalizedAltText, StringComparison.Ordinal)
            && SortOrder == sortOrder)
        {
            return false;
        }

        VariantId = variantId;
        MediaType = mediaType;
        Url = normalizedUrl;
        ThumbnailUrl = normalizedThumbnail;
        AltText = normalizedAltText;
        SortOrder = sortOrder;
        UpdatedOnUtc = CatalogDomainGuard.Utc(occurredOnUtc);
        return true;
    }

    private void Apply(
        Guid? variantId,
        ProductMediaType mediaType,
        string url,
        string? thumbnailUrl,
        string? altText,
        int sortOrder)
    {
        VariantId = variantId;
        MediaType = mediaType;
        Url = ValidateUrl(url, nameof(url));
        ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl)
            ? null
            : ValidateUrl(thumbnailUrl, nameof(thumbnailUrl));
        AltText = CatalogDomainGuard.OptionalOrNull(altText, nameof(altText), 300);
        if (sortOrder is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(sortOrder));
        SortOrder = sortOrder;
        if (!Enum.IsDefined(mediaType))
            throw new ArgumentOutOfRangeException(nameof(mediaType));
    }

    private static string ValidateUrl(string value, string parameterName)
    {
        var normalized = CatalogDomainGuard.Required(value, parameterName, 2_000);
        if (!Uri.TryCreate(normalized, UriKind.RelativeOrAbsolute, out _))
            throw new ArgumentException("Media URL is invalid.", parameterName);
        return normalized;
    }
}

public enum ProductMediaType
{
    Image = 0,
    Video = 1
}
