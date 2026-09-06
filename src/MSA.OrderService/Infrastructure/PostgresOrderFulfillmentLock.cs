using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.OrderService.Application;
using System.Data;

namespace MSA.OrderService.Infrastructure;

internal sealed class PostgresOrderFulfillmentLock(OrderDbContext db) : IOrderFulfillmentLock
{
    public async Task AcquireAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order id is required.", nameof(orderId));

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Fulfillment locks require an active database transaction.");
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@resourceKey, 0));";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resourceKey";
        parameter.Value = $"order-fulfillment:{orderId:N}";
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
