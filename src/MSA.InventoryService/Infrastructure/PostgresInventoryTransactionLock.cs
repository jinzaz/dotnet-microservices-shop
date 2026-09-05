using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.InventoryService.Application.FlashSales;
using System.Data;

namespace MSA.InventoryService.Infrastructure;

internal sealed class PostgresInventoryTransactionLock(InventoryDbContext db) : IInventoryTransactionLock
{
    public async Task AcquireStockItemsAsync(
        IEnumerable<string> skus,
        CancellationToken cancellationToken)
    {
        var resourceKeys = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Select(sku => $"inventory-stock:{sku.Trim().ToUpperInvariant()}");
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
                $"inventory-stock:{sku.Trim().ToUpperInvariant()}"
            ],
            cancellationToken);

    public Task AcquireFlashSaleBuyerAsync(
        Guid campaignId,
        string userId,
        CancellationToken cancellationToken) =>
        AcquireAsync([$"inventory-flash-buyer:{campaignId:N}:{userId}"], cancellationToken);

    private async Task AcquireAsync(
        IEnumerable<string> resourceKeys,
        CancellationToken cancellationToken)
    {
        var keys = resourceKeys
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
            return;

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Inventory locks require an active database transaction.");
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SELECT pg_advisory_xact_lock(hashtextextended(value, 0))
            FROM unnest(@resourceKeys) AS keys(value)
            ORDER BY value;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resourceKeys";
        parameter.Value = keys;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }
}
