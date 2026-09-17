namespace MSA.BasketService.Domain;

public sealed class BasketProductSnapshot
{
    private BasketProductSnapshot()
    {
    }

    public BasketProductSnapshot(
        Guid productId,
        Guid variantId,
        string sku,
        string productName,
        string variantName,
        string attributesJson,
        decimal price,
        string currency,
        bool isAvailable,
        long sourceRevision,
        DateTimeOffset effectiveOnUtc)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));
        if (variantId == Guid.Empty)
            throw new ArgumentException("Variant id is required.", nameof(variantId));

        ProductId = productId;
        VariantId = variantId;
        Apply(sku, productName, variantName, attributesJson, price, currency, isAvailable, sourceRevision, effectiveOnUtc);
    }

    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string VariantName { get; private set; } = string.Empty;
    public string AttributesJson { get; private set; } = "{}";
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; }
    public long SourceRevision { get; private set; }
    public DateTimeOffset EffectiveOnUtc { get; private set; }

    public bool Apply(
        string sku,
        string productName,
        string variantName,
        string attributesJson,
        decimal price,
        string currency,
        bool isAvailable,
        long sourceRevision,
        DateTimeOffset effectiveOnUtc)
    {
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        if (sourceRevision < SourceRevision
            || sourceRevision == SourceRevision && effectiveOnUtc <= EffectiveOnUtc)
        {
            return false;
        }

        // 先完成全部校验再赋值，非法事件不会留下半更新的投影。
        var normalizedSku = Basket.NormalizeSku(sku);
        var normalizedProductName = NormalizeName(productName, nameof(productName));
        var normalizedVariantName = NormalizeName(variantName, nameof(variantName));
        if (price is < 0 or > 9_999_999_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(price));
        var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(currency));

        Sku = normalizedSku;
        ProductName = normalizedProductName;
        VariantName = normalizedVariantName;
        AttributesJson = string.IsNullOrWhiteSpace(attributesJson) ? "{}" : attributesJson.Trim();
        Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        Currency = normalizedCurrency;
        IsAvailable = isAvailable;
        SourceRevision = sourceRevision;
        EffectiveOnUtc = effectiveOnUtc;
        return true;
    }

    public ProductSelection ToSelection() =>
        new(
            ProductId,
            VariantId,
            Sku,
            ProductName,
            VariantName,
            AttributesJson,
            Price,
            Currency,
            IsAvailable,
            SourceRevision);

    private static string NormalizeName(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 200)
            throw new ArgumentException("Name must contain 1-200 characters.", parameterName);
        return normalized;
    }
}
