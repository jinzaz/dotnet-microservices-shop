namespace MSA.InventoryService.Application.FlashSales;

public interface IInventoryTransactionLock
{
    Task AcquireStockItemsAsync(IEnumerable<string> skus, CancellationToken cancellationToken);
    Task AcquireStockAdjustmentAsync(
        string sku,
        string operatorId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task AcquireOrderReservationAsync(Guid orderId, CancellationToken cancellationToken);
    Task AcquireFlashSaleBuyerAsync(Guid campaignId, string userId, CancellationToken cancellationToken);
}

public interface IFlashSaleStockStore
{
    Task<FlashSaleReservationAttempt> TryReserveAsync(
        Guid campaignId,
        string sku,
        int quantity,
        Guid orderId,
        CancellationToken cancellationToken);
}

public sealed record FlashSaleReservationAttempt(bool Succeeded, long? StockBucketId, string? RejectionReason)
{
    public static FlashSaleReservationAttempt Reserved(long stockBucketId) => new(true, stockBucketId, null);
    public static FlashSaleReservationAttempt Rejected(string reason) => new(false, null, reason);
}

public static class FlashSaleCacheKeys
{
    public static string Availability(Guid campaignId) => $"flash-sale:availability:{campaignId:N}";
}
