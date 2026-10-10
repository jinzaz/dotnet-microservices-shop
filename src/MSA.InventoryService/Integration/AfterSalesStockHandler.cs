using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.InventoryService.Application;
using MSA.InventoryService.Domain;
using MSA.InventoryService.Infrastructure;

namespace MSA.InventoryService.Integration;

/// <summary>
/// 售后的库存动作：换货 / 补寄的新品沿用「预占 → 出库提交 / 释放」生命周期，退货验收后入库为实物增加。
/// 不另建单据表：库存流水按（类型, 售后单号, SKU）唯一，流水本身就是幂等记录，重放与乱序都只看已有流水。
/// </summary>
internal sealed class AfterSalesStockHandler(
    InventoryDbContext db,
    IOutboxStore outbox,
    IInventoryTransactionLock transactionLock,
    TimeProvider clock)
    : InventoryIntegrationEventHandler<AfterSalesStockRequestedIntegrationEvent>(db, outbox, "inventory.after-sales-stock.v1")
{
    protected override async Task HandleCoreAsync(
        AfterSalesStockRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (await AfterSalesStock.ApplyAsync(Db, transactionLock, integrationEvent, clock.GetUtcNow(), cancellationToken) is { } result)
            Publish(result);
    }
}

internal static class AfterSalesStock
{
    /// <returns>The reservation answer for Ordering; null for operations that need no answer.</returns>
    public static async Task<AfterSalesStockReservationResultIntegrationEvent?> ApplyAsync(
        InventoryDbContext db,
        IInventoryTransactionLock transactionLock,
        AfterSalesStockRequestedIntegrationEvent integrationEvent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var items = integrationEvent.Items
            .GroupBy(item => StockItem.NormalizeSku(item.Sku))
            .Select(group => (Sku: group.Key, Quantity: group.Sum(item => item.Quantity)))
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .ToArray();
        if (items.Length == 0 || items.Any(item => item.Quantity <= 0))
            throw new InvalidOperationException($"After-sales stock request {integrationEvent.RequestId} has no valid items.");

        var skus = items.Select(item => item.Sku).ToArray();
        await transactionLock.AcquireStockItemsAsync(skus, cancellationToken);
        var recorded = await db.StockMovements
            .Where(movement => movement.ReferenceType == StockMovementReferenceType.AfterSales
                && movement.ReferenceId == integrationEvent.RequestId)
            .Select(movement => movement.Type)
            .Distinct()
            .ToListAsync(cancellationToken);
        var stock = await db.StockItems
            .Where(item => skus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        var number = integrationEvent.RequestNumber;

        switch (integrationEvent.Operation)
        {
            case AfterSalesStockOperations.Reserve:
                if (recorded.Contains(StockMovementType.InventoryReserved))
                    return Result(integrationEvent.RequestId, null);
                var shortage = items.FirstOrDefault(item => !stock.TryGetValue(item.Sku, out var stockItem) || stockItem.Available < item.Quantity);
                if (shortage.Sku is not null)
                    return Result(integrationEvent.RequestId, $"SKU {shortage.Sku} 可用库存不足 {shortage.Quantity} 件");
                foreach (var item in items)
                {
                    stock[item.Sku].Reserve(item.Quantity);
                    Record(stock[item.Sku], StockMovementType.InventoryReserved, 0, item.Quantity, $"售后 {number} 锁定换新/补寄库存");
                }
                return Result(integrationEvent.RequestId, null);

            case AfterSalesStockOperations.Commit:
                if (recorded.Contains(StockMovementType.InventoryCommitted))
                    return null;
                if (!recorded.Contains(StockMovementType.InventoryReserved) || recorded.Contains(StockMovementType.InventoryReleased))
                    throw new InvalidOperationException($"After-sales {number} has no live stock hold to ship.");
                foreach (var item in items)
                {
                    stock[item.Sku].Commit(item.Quantity);
                    Record(stock[item.Sku], StockMovementType.InventoryCommitted, -item.Quantity, -item.Quantity, $"售后 {number} 新品出库");
                }
                return null;

            case AfterSalesStockOperations.Release:
                // 从未锁到、已释放或已出库都无需归还。
                if (!recorded.Contains(StockMovementType.InventoryReserved)
                    || recorded.Contains(StockMovementType.InventoryReleased)
                    || recorded.Contains(StockMovementType.InventoryCommitted))
                {
                    return null;
                }
                foreach (var item in items)
                {
                    stock[item.Sku].Release(item.Quantity);
                    Record(stock[item.Sku], StockMovementType.InventoryReleased, 0, -item.Quantity, $"售后 {number} 关闭，释放锁定库存");
                }
                return null;

            case AfterSalesStockOperations.Restock:
                if (recorded.Contains(StockMovementType.ReturnRestocked))
                    return null;
                foreach (var item in items)
                {
                    if (!stock.TryGetValue(item.Sku, out var stockItem))
                    {
                        stockItem = new StockItem(Guid.NewGuid(), item.Sku, item.Quantity);
                        db.StockItems.Add(stockItem);
                    }
                    else
                    {
                        stockItem.Increase(item.Quantity);
                    }
                    Record(stockItem, StockMovementType.ReturnRestocked, item.Quantity, 0, $"售后 {number} 退货验收入库", integrationEvent.OperatorId);
                }
                return null;

            default:
                throw new InvalidOperationException($"Unknown after-sales stock operation '{integrationEvent.Operation}'.");
        }

        void Record(StockItem stockItem, StockMovementType type, int onHandDelta, int reservedDelta, string reason, string? operatorId = null) =>
            db.StockMovements.Add(StockMovement.Record(
                stockItem,
                type,
                onHandDelta,
                reservedDelta,
                StockMovementReferenceType.AfterSales,
                integrationEvent.RequestId,
                reason,
                now,
                operatorId));
    }

    private static AfterSalesStockReservationResultIntegrationEvent Result(Guid requestId, string? shortage) =>
        new()
        {
            RequestId = requestId,
            Reserved = shortage is null,
            Reason = shortage
        };
}
