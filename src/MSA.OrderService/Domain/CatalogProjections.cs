namespace MSA.OrderService.Domain;

public sealed class OrderProductProjection
{
    private OrderProductProjection()
    {
    }

    // Compatibility constructor for snapshots emitted before products gained variants.
    public OrderProductProjection(
        Guid productId,
        string sku,
        string name,
        decimal price,
        string currency,
        bool active,
        Guid? categoryId = null,
        long sourceRevision = 0,
        DateTimeOffset? effectiveOnUtc = null)
        : this(
            productId,
            productId,
            sku,
            name,
            name,
            "{}",
            price,
            currency,
            active,
            categoryId,
            sourceRevision,
            effectiveOnUtc)
    {
    }

    public OrderProductProjection(
        Guid variantId,
        Guid productId,
        string sku,
        string productName,
        string variantName,
        string attributesJson,
        decimal price,
        string currency,
        bool active,
        Guid? categoryId = null,
        long sourceRevision = 0,
        DateTimeOffset? effectiveOnUtc = null)
    {
        if (variantId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Product and variant ids are required.");
        VariantId = variantId;
        ProductId = productId;
        Sku = sku.Trim();
        ProductName = productName.Trim();
        VariantName = variantName.Trim();
        AttributesJson = string.IsNullOrWhiteSpace(attributesJson) ? "{}" : attributesJson.Trim();
        Price = Money.Round(price);
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = active;
        CategoryId = categoryId;
        SourceRevision = sourceRevision;
        UpdatedOnUtc = effectiveOnUtc ?? DateTimeOffset.UtcNow;
    }

    public Guid VariantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string VariantName { get; private set; } = string.Empty;
    public string AttributesJson { get; private set; } = "{}";
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public long SourceRevision { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public string Name => ProductName;

    public void Update(string name, decimal price, string currency, bool active) =>
        Apply(name, name, "{}", price, currency, active, CategoryId, SourceRevision + 1, DateTimeOffset.UtcNow);

    public bool Apply(
        string name,
        decimal price,
        string currency,
        bool active,
        Guid? categoryId,
        long sourceRevision,
        DateTimeOffset effectiveOnUtc) =>
        Apply(name, name, "{}", price, currency, active, categoryId, sourceRevision, effectiveOnUtc);

    public bool Apply(
        string productName,
        string variantName,
        string attributesJson,
        decimal price,
        string currency,
        bool active,
        Guid? categoryId,
        long sourceRevision,
        DateTimeOffset effectiveOnUtc)
    {
        if (sourceRevision < SourceRevision
            || sourceRevision == SourceRevision && effectiveOnUtc <= UpdatedOnUtc)
        {
            return false;
        }

        ProductName = productName.Trim();
        VariantName = variantName.Trim();
        AttributesJson = string.IsNullOrWhiteSpace(attributesJson) ? "{}" : attributesJson.Trim();
        Price = Money.Round(price);
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = active;
        CategoryId = categoryId;
        SourceRevision = sourceRevision;
        UpdatedOnUtc = effectiveOnUtc;
        return true;
    }
}

public sealed class InventoryAllocationProjection
{
    private InventoryAllocationProjection()
    {
    }

    public InventoryAllocationProjection(
        Guid allocationId,
        Guid offerId,
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int perUserLimit,
        DateTimeOffset updatedOnUtc)
    {
        if (allocationId == Guid.Empty || offerId == Guid.Empty)
            throw new ArgumentException("Allocation and offer ids are required.");
        AllocationId = allocationId;
        OfferId = offerId;
        ApplyOpen(sku, startsOnUtc, endsOnUtc, perUserLimit, updatedOnUtc);
    }

    private InventoryAllocationProjection(
        Guid allocationId,
        Guid offerId,
        string sku,
        DateTimeOffset closedOnUtc)
    {
        AllocationId = allocationId;
        OfferId = offerId;
        Sku = sku;
        StartsOnUtc = DateTimeOffset.UnixEpoch;
        EndsOnUtc = DateTimeOffset.UnixEpoch.AddTicks(1);
        PerUserLimit = 1;
        IsOpen = false;
        ClosedOnUtc = closedOnUtc;
        UpdatedOnUtc = closedOnUtc;
    }

    public Guid AllocationId { get; private set; }
    public Guid OfferId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int PerUserLimit { get; private set; }
    public bool IsOpen { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public long Version { get; private set; }

    public bool AcceptsOrders(DateTimeOffset now) =>
        IsOpen && StartsOnUtc <= now && now < EndsOnUtc;

    public static InventoryAllocationProjection CreateClosed(
        Guid allocationId,
        Guid offerId,
        string sku,
        DateTimeOffset closedOnUtc) =>
        new(allocationId, offerId, sku, closedOnUtc);

    public void ApplyOpen(
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int perUserLimit,
        DateTimeOffset updatedOnUtc)
    {
        if (UpdatedOnUtc > updatedOnUtc)
            return;
        Sku = sku.Trim().ToUpperInvariant();
        StartsOnUtc = startsOnUtc;
        EndsOnUtc = endsOnUtc;
        PerUserLimit = perUserLimit;
        IsOpen = !ClosedOnUtc.HasValue || ClosedOnUtc < updatedOnUtc;
        if (IsOpen)
            ClosedOnUtc = null;
        UpdatedOnUtc = updatedOnUtc;
        Version++;
    }

    public void Close(DateTimeOffset closedOnUtc)
    {
        if (ClosedOnUtc.HasValue && ClosedOnUtc.Value >= closedOnUtc)
            return;
        IsOpen = false;
        ClosedOnUtc = closedOnUtc;
        UpdatedOnUtc = closedOnUtc;
        Version++;
    }
}
