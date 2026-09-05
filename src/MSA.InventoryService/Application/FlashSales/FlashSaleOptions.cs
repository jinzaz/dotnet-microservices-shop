namespace MSA.InventoryService.Application.FlashSales;

public sealed class FlashSaleOptions
{
    public const string SectionName = "FlashSale";

    public int LargeStockThreshold { get; set; } = 1_000;
    public int DefaultBucketCount { get; set; } = 32;
    public int MaximumBucketCount { get; set; } = 128;
    public int ReservationSkipLockedRetries { get; set; } = 3;
    public TimeSpan AvailabilityCacheDuration { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ExpiryScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int ExpiryBatchSize { get; set; } = 100;
}
