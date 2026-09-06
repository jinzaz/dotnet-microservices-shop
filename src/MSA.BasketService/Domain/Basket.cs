using MSA.Infrastrucute.Entities;

namespace MSA.BasketService.Domain;

public static class BasketLimits
{
    public const int MaximumSupportedLineQuantity = 999;
    public const int MaximumSupportedDistinctItems = 200;
    public const int MaximumSupportedCouponCodes = 5;
}

public sealed record ProductSelection(
    Guid ProductId,
    string Sku,
    string Name,
    decimal UnitPrice,
    string Currency,
    bool IsAvailable,
    long SourceRevision);

public sealed class Basket : AggregateRoot<Guid>
{
    private readonly List<BasketItem> _items = [];
    private readonly List<BasketCoupon> _coupons = [];

    private Basket()
    {
    }

    public Basket(Guid id, string userId, DateTimeOffset createdOnUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Basket id is required.", nameof(id));

        Id = id;
        UserId = NormalizeUserId(userId);
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        Version = 0;
    }

    public string UserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<BasketCoupon> Coupons => _coupons.AsReadOnly();

    public void AddOrIncreaseItem(
        ProductSelection product,
        int quantity,
        int maximumDistinctItems,
        int maximumLineQuantity,
        long? expectedVersion,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(product);
        EnsureExpectedVersion(expectedVersion);
        EnsureAvailable(product);
        ValidatePositiveQuantity(quantity, maximumLineQuantity);

        var normalizedSku = NormalizeSku(product.Sku);
        var existing = _items.SingleOrDefault(item => item.Sku == normalizedSku);
        if (existing is null)
        {
            if (_items.Count >= maximumDistinctItems)
                throw new BasketRuleException($"A basket cannot contain more than {maximumDistinctItems} distinct items.");

            _items.Add(BasketItem.Create(Id, product, quantity, occurredOnUtc));
        }
        else
        {
            var newQuantity = checked(existing.Quantity + quantity);
            ValidatePositiveQuantity(newQuantity, maximumLineQuantity);
            existing.SetQuantity(newQuantity, product, occurredOnUtc);
        }

        Touch(occurredOnUtc);
    }

    public void SetItemQuantity(
        string sku,
        int quantity,
        ProductSelection? currentProduct,
        int maximumLineQuantity,
        long? expectedVersion,
        DateTimeOffset occurredOnUtc)
    {
        ValidatePositiveQuantity(quantity, maximumLineQuantity);
        var normalizedSku = NormalizeSku(sku);
        var item = _items.SingleOrDefault(value => value.Sku == normalizedSku)
            ?? throw new KeyNotFoundException($"Basket item '{normalizedSku}' was not found.");

        if (item.Quantity == quantity)
            return;

        EnsureExpectedVersion(expectedVersion);
        if (quantity > item.Quantity && (currentProduct is null || !currentProduct.IsAvailable))
            throw new ProductUnavailableException(normalizedSku);

        item.SetQuantity(quantity, currentProduct, occurredOnUtc);
        Touch(occurredOnUtc);
    }

    public bool RemoveItem(string sku, long? expectedVersion, DateTimeOffset occurredOnUtc)
    {
        var item = _items.SingleOrDefault(value => value.Sku == NormalizeSku(sku));
        if (item is null)
            return false;

        EnsureExpectedVersion(expectedVersion);
        _items.Remove(item);
        Touch(occurredOnUtc);
        return true;
    }

    public bool Clear(long? expectedVersion, DateTimeOffset occurredOnUtc)
    {
        if (_items.Count == 0 && _coupons.Count == 0)
            return false;

        EnsureExpectedVersion(expectedVersion);
        _items.Clear();
        _coupons.Clear();
        Touch(occurredOnUtc);
        return true;
    }

    public bool ReplaceCouponCodes(
        IEnumerable<string> couponCodes,
        int maximumCouponCodes,
        long? expectedVersion,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(couponCodes);
        var normalized = couponCodes
            .Select(NormalizeCouponCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length > maximumCouponCodes)
            throw new BasketRuleException($"A basket cannot contain more than {maximumCouponCodes} coupon codes.");

        var existing = _coupons
            .Select(value => value.Code)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (existing.SequenceEqual(normalized, StringComparer.Ordinal))
            return false;

        EnsureExpectedVersion(expectedVersion);
        _coupons.Clear();
        _coupons.AddRange(normalized.Select(code => new BasketCoupon(Guid.NewGuid(), Id, code, occurredOnUtc)));
        Touch(occurredOnUtc);
        return true;
    }

    private void EnsureExpectedVersion(long? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != Version)
            throw new BasketVersionConflictException(expectedVersion.Value, Version);
    }

    private void Touch(DateTimeOffset occurredOnUtc)
    {
        if (occurredOnUtc < UpdatedOnUtc)
            occurredOnUtc = UpdatedOnUtc;
        UpdatedOnUtc = occurredOnUtc;
        Version = checked(Version + 1);
    }

    private static void EnsureAvailable(ProductSelection product)
    {
        if (!product.IsAvailable)
            throw new ProductUnavailableException(product.Sku);
    }

    private static void ValidatePositiveQuantity(int quantity, int maximumLineQuantity)
    {
        if (quantity < 1 || quantity > maximumLineQuantity)
            throw new BasketRuleException($"Item quantity must be between 1 and {maximumLineQuantity}.");
    }

    public static string NormalizeSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        var normalized = sku.Trim().ToUpperInvariant();
        if (normalized.Length > 80)
            throw new ArgumentException("SKU cannot exceed 80 characters.", nameof(sku));
        return normalized;
    }

    public static string NormalizeCouponCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > 80)
            throw new ArgumentException("Coupon code cannot exceed 80 characters.", nameof(code));
        return normalized;
    }

    private static string NormalizeUserId(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var normalized = userId.Trim();
        if (normalized.Length > 200)
            throw new ArgumentException("User id cannot exceed 200 characters.", nameof(userId));
        return normalized;
    }
}

public sealed class BasketItem : Entity<Guid>
{
    private BasketItem()
    {
    }

    private BasketItem(
        Guid id,
        Guid basketId,
        ProductSelection product,
        int quantity,
        DateTimeOffset occurredOnUtc)
    {
        Id = id;
        BasketId = basketId;
        ApplyProduct(product);
        Quantity = quantity;
        AddedOnUtc = occurredOnUtc;
        UpdatedOnUtc = occurredOnUtc;
    }

    public Guid BasketId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductNameAtSelection { get; private set; } = string.Empty;
    public decimal UnitPriceAtSelection { get; private set; }
    public string CurrencyAtSelection { get; private set; } = string.Empty;
    public long ProductRevisionAtSelection { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset AddedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    internal static BasketItem Create(
        Guid basketId,
        ProductSelection product,
        int quantity,
        DateTimeOffset occurredOnUtc) =>
        new(Guid.NewGuid(), basketId, product, quantity, occurredOnUtc);

    internal void SetQuantity(
        int quantity,
        ProductSelection? currentProduct,
        DateTimeOffset occurredOnUtc)
    {
        Quantity = quantity;
        if (currentProduct is not null && currentProduct.IsAvailable)
            ApplyProduct(currentProduct);
        UpdatedOnUtc = occurredOnUtc;
    }

    private void ApplyProduct(ProductSelection product)
    {
        ProductId = product.ProductId;
        Sku = Basket.NormalizeSku(product.Sku);
        ProductNameAtSelection = product.Name.Trim();
        UnitPriceAtSelection = decimal.Round(product.UnitPrice, 2, MidpointRounding.AwayFromZero);
        CurrencyAtSelection = product.Currency.Trim().ToUpperInvariant();
        ProductRevisionAtSelection = product.SourceRevision;
    }
}

public sealed class BasketCoupon : Entity<Guid>
{
    private BasketCoupon()
    {
    }

    internal BasketCoupon(Guid id, Guid basketId, string code, DateTimeOffset addedOnUtc)
    {
        Id = id;
        BasketId = basketId;
        Code = Basket.NormalizeCouponCode(code);
        AddedOnUtc = addedOnUtc;
    }

    public Guid BasketId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset AddedOnUtc { get; private set; }
}

public sealed class BasketVersionConflictException(long expectedVersion, long actualVersion)
    : InvalidOperationException($"Basket version {expectedVersion} does not match current version {actualVersion}.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}

public sealed class BasketRuleException(string message) : InvalidOperationException(message);

public sealed class ProductUnavailableException(string sku)
    : InvalidOperationException($"Product '{Basket.NormalizeSku(sku)}' is unavailable.");
