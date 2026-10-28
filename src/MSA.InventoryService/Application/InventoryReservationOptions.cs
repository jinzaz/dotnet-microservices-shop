namespace MSA.InventoryService.Application;

public sealed class InventoryReservationOptions
{
    public const string SectionName = "InventoryReservations";

    public TimeSpan RegularLifetime { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan OfferLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 预占过期时间 = 订单支付截止时间 + 宽限期。消息积压时，截止前已确认订单的提交库存消息可能晚到，
    /// 宽限期内预占不会被过期清理释放。超时取消由 Saga 主动发释放消息，过期清理只是兜底，晚一点释放无妨。
    /// </summary>
    public TimeSpan ExpiryGracePeriod { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>过期预占的扫描间隔。</summary>
    public TimeSpan ExpiryScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>单次扫描最多释放多少个过期预占。</summary>
    public int ExpiryBatchSize { get; set; } = 100;
}
