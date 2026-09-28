using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.Persistence.Postgres;

namespace MSA.InventoryService.Infrastructure;

internal sealed class PostgresInventoryTransactionLock(InventoryDbContext db) : IInventoryTransactionLock
{
    public async Task AcquireStockItemsAsync(
        IEnumerable<string> skus,
        CancellationToken cancellationToken)
    {
        var resourceKeys = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Select(sku => $"inventory-stock:{StockItem.NormalizeSku(sku)}");
        await AcquireAsync(resourceKeys, cancellationToken);
    }

    public Task AcquireOrderReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
        AcquireAsync([$"inventory-order:{orderId:N}"], cancellationToken);

    public Task AcquireStockAdjustmentAsync(
        string sku,
        string operatorId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        AcquireAsync(
            [
                $"inventory-adjustment:{operatorId.Trim()}:{idempotencyKey.Trim()}",
                $"inventory-stock:{StockItem.NormalizeSku(sku)}"
            ],
            cancellationToken);

    public Task AcquireAllocationBuyerAsync(
        Guid allocationId,
        string userId,
        CancellationToken cancellationToken) =>
        AcquireAsync([$"inventory-allocation-buyer:{allocationId:N}:{userId}"], cancellationToken);

    /// <summary>键去重并按固定顺序加锁：顺序一致才不会两个事务互相等待。</summary>
    private Task AcquireAsync(IEnumerable<string> resourceKeys, CancellationToken cancellationToken) =>
        db.AcquireTransactionLocksAsync(resourceKeys, cancellationToken);
}
