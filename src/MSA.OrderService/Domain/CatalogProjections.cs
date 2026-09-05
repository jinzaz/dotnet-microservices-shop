namespace MSA.OrderService.Domain;

public sealed class OrderProductProjection
{
    private OrderProductProjection()
    {
    }

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
    {
        ProductId = productId;
        Sku = sku.Trim();
        Name = name.Trim();
        Price = Money.Round(price);
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = active;
        CategoryId = categoryId;
        SourceRevision = sourceRevision;
        UpdatedOnUtc = effectiveOnUtc ?? DateTimeOffset.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public long SourceRevision { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    public void Update(string name, decimal price, string currency, bool active) =>
        Apply(name, price, currency, active, CategoryId, SourceRevision + 1, DateTimeOffset.UtcNow);

    public bool Apply(
        string name,
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

        Name = name.Trim();
        Price = Money.Round(price);
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = active;
        CategoryId = categoryId;
        SourceRevision = sourceRevision;
        UpdatedOnUtc = effectiveOnUtc;
        return true;
    }
}

public sealed class FlashSaleCampaignProjection
{
    private FlashSaleCampaignProjection()
    {
    }

    public FlashSaleCampaignProjection(
        Guid campaignId,
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        string strategy,
        DateTimeOffset updatedOnUtc)
    {
        CampaignId = campaignId;
        ApplyOpen(sku, startsOnUtc, endsOnUtc, purchaseLimit, strategy, updatedOnUtc);
    }

    private FlashSaleCampaignProjection(Guid campaignId, DateTimeOffset closedOnUtc)
    {
        CampaignId = campaignId;
        Sku = $"closed:{campaignId:N}";
        StartsOnUtc = DateTimeOffset.UnixEpoch;
        EndsOnUtc = DateTimeOffset.UnixEpoch.AddTicks(1);
        PurchaseLimit = 1;
        Strategy = "Unknown";
        IsOpen = false;
        ClosedOnUtc = closedOnUtc;
        UpdatedOnUtc = closedOnUtc;
    }

    public Guid CampaignId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int PurchaseLimit { get; private set; }
    public string Strategy { get; private set; } = string.Empty;
    public bool IsOpen { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public long Version { get; private set; }

    public bool AcceptsOrders(DateTimeOffset now) =>
        IsOpen && StartsOnUtc <= now && now < EndsOnUtc;

    public static FlashSaleCampaignProjection CreateClosed(
        Guid campaignId,
        DateTimeOffset closedOnUtc) =>
        new(campaignId, closedOnUtc);

    public void ApplyOpen(
        string sku,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        string strategy,
        DateTimeOffset updatedOnUtc)
    {
        if (UpdatedOnUtc > updatedOnUtc)
            return;

        Sku = sku;
        StartsOnUtc = startsOnUtc;
        EndsOnUtc = endsOnUtc;
        PurchaseLimit = purchaseLimit;
        Strategy = strategy;
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
