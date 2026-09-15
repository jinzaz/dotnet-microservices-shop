namespace MSA.InventoryService.Application;

public interface IInventoryTransactionLock
{
    Task AcquireStockItemsAsync(IEnumerable<string> skus, CancellationToken cancellationToken);
    Task AcquireStockAdjustmentAsync(
        string sku,
        string operatorId,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task AcquireOrderReservationAsync(Guid orderId, CancellationToken cancellationToken);
    Task AcquireAllocationBuyerAsync(
        Guid allocationId,
        string userId,
        CancellationToken cancellationToken);
}
