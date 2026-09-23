namespace MSA.InventoryService.Application;

public sealed class InventoryReservationOptions
{
    public const string SectionName = "InventoryReservations";

    public TimeSpan RegularLifetime { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan OfferLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>过期预占的扫描间隔。</summary>
    public TimeSpan ExpiryScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>单次扫描最多释放多少个过期预占。</summary>
    public int ExpiryBatchSize { get; set; } = 100;
}
