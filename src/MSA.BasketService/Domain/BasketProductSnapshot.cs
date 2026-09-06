namespace MSA.BasketService.Domain;

public sealed class BasketProductSnapshot
{
    private BasketProductSnapshot()
    {
    }

    public BasketProductSnapshot(
        Guid productId,
        string sku,
        string name,
        decimal price,
        string currency,
        bool isAvailable,
        long sourceRevision,
        DateTimeOffset effectiveOnUtc)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("Product id is required.", nameof(productId));

        ProductId = productId;
        Sku = Basket.NormalizeSku(sku);
        Apply(sku, name, price, currency, isAvailable, sourceRevision, effectiveOnUtc);
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; }
    public long SourceRevision { get; private set; }
    public DateTimeOffset EffectiveOnUtc { get; private set; }

    public bool Apply(
        string sku,
        string name,
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
            // 同一商品只接受更新的修订，修订相同时用生效时间抵御重复或乱序事件。
            return false;
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is 0 or > 200)
            throw new ArgumentException("Product name must contain 1-200 characters.", nameof(name));
        if (price is < 0 or > 9_999_999_999_999_999.99m)
            throw new ArgumentOutOfRangeException(nameof(price));
        var normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(currency));

        Sku = Basket.NormalizeSku(sku);
        Name = normalizedName;
        Price = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
        Currency = normalizedCurrency;
        IsAvailable = isAvailable;
        SourceRevision = sourceRevision;
        EffectiveOnUtc = effectiveOnUtc;
        return true;
    }

    public ProductSelection ToSelection() =>
        new(ProductId, Sku, Name, Price, Currency, IsAvailable, SourceRevision);
}
