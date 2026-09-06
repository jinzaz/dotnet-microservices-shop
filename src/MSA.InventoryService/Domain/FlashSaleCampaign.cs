using MSA.Infrastrucute.Entities;

namespace MSA.InventoryService.Domain;

public sealed class FlashSaleCampaign : AggregateRoot<Guid>
{
    private readonly List<FlashSaleStockBucket> _stockBuckets = [];

    private FlashSaleCampaign()
    {
    }

    private FlashSaleCampaign(
        Guid id,
        string sku,
        int totalQuantity,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        FlashSaleStockStrategy strategy,
        int bucketCount,
        DateTimeOffset createdOnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        if (id == Guid.Empty)
            throw new ArgumentException("Campaign id is required.", nameof(id));
        if (totalQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalQuantity));
        if (startsOnUtc >= endsOnUtc)
            throw new ArgumentException("The campaign end must be after its start.", nameof(endsOnUtc));
        if (purchaseLimit <= 0 || purchaseLimit > totalQuantity)
            throw new ArgumentOutOfRangeException(nameof(purchaseLimit));
        if (strategy == FlashSaleStockStrategy.SingleRow && bucketCount != 1)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), "Single-row stock requires exactly one bucket.");
        if (strategy == FlashSaleStockStrategy.Bucketed
            && (bucketCount < 2
                || bucketCount > 128
                || bucketCount > totalQuantity
                || bucketCount > totalQuantity / purchaseLimit))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bucketCount),
                "Bucketed stock requires 2-128 buckets, each large enough for one maximum-size purchase.");
        }

        Id = id;
        Sku = sku.Trim().ToUpperInvariant();
        TotalQuantity = totalQuantity;
        StartsOnUtc = startsOnUtc;
        EndsOnUtc = endsOnUtc;
        PurchaseLimit = purchaseLimit;
        Strategy = strategy;
        BucketCount = bucketCount;
        Status = FlashSaleCampaignStatus.Open;
        CreatedOnUtc = createdOnUtc;

        var baseQuantity = totalQuantity / bucketCount;
        var remainder = totalQuantity % bucketCount;
        // 将热点库存均匀拆分到多个桶，余数固定分配给前几个桶，保证总量精确且创建结果确定。
        for (var bucketNumber = 0; bucketNumber < bucketCount; bucketNumber++)
        {
            var allocated = baseQuantity + (bucketNumber < remainder ? 1 : 0);
            _stockBuckets.Add(new FlashSaleStockBucket(id, bucketNumber, allocated));
        }
    }

    public string Sku { get; private set; } = string.Empty;
    public int TotalQuantity { get; private set; }
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int PurchaseLimit { get; private set; }
    public FlashSaleStockStrategy Strategy { get; private set; }
    public int BucketCount { get; private set; }
    public FlashSaleCampaignStatus Status { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset? ClosedOnUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<FlashSaleStockBucket> StockBuckets => _stockBuckets;

    public static FlashSaleCampaign Create(
        Guid id,
        string sku,
        int totalQuantity,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int purchaseLimit,
        FlashSaleStockStrategy strategy,
        int bucketCount,
        DateTimeOffset createdOnUtc) =>
        new(id, sku, totalQuantity, startsOnUtc, endsOnUtc, purchaseLimit, strategy, bucketCount, createdOnUtc);

    public bool AcceptsOrders(DateTimeOffset now) =>
        Status == FlashSaleCampaignStatus.Open && StartsOnUtc <= now && now < EndsOnUtc;

    public int Close(DateTimeOffset now)
    {
        if (Status == FlashSaleCampaignStatus.Closed)
            return 0;

        var releasedQuantity = _stockBuckets.Sum(bucket => bucket.ReleaseAvailable());
        Status = FlashSaleCampaignStatus.Closed;
        ClosedOnUtc = now;
        Version++;
        return releasedQuantity;
    }
}

public sealed class FlashSaleStockBucket
{
    private FlashSaleStockBucket()
    {
    }

    internal FlashSaleStockBucket(Guid campaignId, int bucketNumber, int allocatedQuantity)
    {
        if (campaignId == Guid.Empty)
            throw new ArgumentException("Campaign id is required.", nameof(campaignId));
        if (bucketNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(bucketNumber));
        if (allocatedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(allocatedQuantity));

        CampaignId = campaignId;
        BucketNumber = bucketNumber;
        AllocatedQuantity = allocatedQuantity;
    }

    public long Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public int BucketNumber { get; private set; }
    public int AllocatedQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public int SoldQuantity { get; private set; }
    public int ReleasedQuantity { get; private set; }
    public int AvailableQuantity => AllocatedQuantity - ReservedQuantity - SoldQuantity - ReleasedQuantity;
    public long Version { get; private set; }

    public bool TryReserve(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (AvailableQuantity < quantity)
            return false;

        ReservedQuantity = checked(ReservedQuantity + quantity);
        Version++;
        return true;
    }

    public void Commit(int quantity)
    {
        EnsureReservedQuantity(quantity);
        ReservedQuantity -= quantity;
        SoldQuantity = checked(SoldQuantity + quantity);
        Version++;
    }

    public void Release(int quantity, bool campaignClosed)
    {
        EnsureReservedQuantity(quantity);
        ReservedQuantity -= quantity;
        if (campaignClosed)
            ReleasedQuantity = checked(ReleasedQuantity + quantity);
        Version++;
    }

    internal int ReleaseAvailable()
    {
        var quantity = AvailableQuantity;
        if (quantity == 0)
            return 0;

        ReleasedQuantity = checked(ReleasedQuantity + quantity);
        Version++;
        return quantity;
    }

    internal void TransferAvailableTo(FlashSaleStockBucket target, int quantity)
    {
        // 只转移尚未预占的配额，两个桶版本同时推进，供持久化层检测并发再平衡冲突。
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(this, target))
            throw new InvalidOperationException("A stock bucket cannot transfer inventory to itself.");
        if (CampaignId != target.CampaignId)
            throw new InvalidOperationException("Stock can only move between buckets in the same campaign.");
        if (quantity <= 0 || AvailableQuantity < quantity)
            throw new InvalidOperationException("The source bucket does not have enough available inventory.");

        AllocatedQuantity -= quantity;
        target.AllocatedQuantity = checked(target.AllocatedQuantity + quantity);
        Version++;
        target.Version++;
    }

    private void EnsureReservedQuantity(int quantity)
    {
        if (quantity <= 0 || ReservedQuantity < quantity)
            throw new InvalidOperationException("The stock bucket does not contain the requested reservation quantity.");
    }
}

public enum FlashSaleStockStrategy
{
    SingleRow = 0,
    Bucketed = 1
}

public enum FlashSaleCampaignStatus
{
    Open = 0,
    Closed = 1
}
