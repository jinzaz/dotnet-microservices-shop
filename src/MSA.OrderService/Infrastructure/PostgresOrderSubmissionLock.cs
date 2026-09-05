using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.OrderService.Application;
using System.Data;

namespace MSA.OrderService.Infrastructure;

internal sealed class PostgresOrderSubmissionLock(OrderDbContext db) : IOrderSubmissionLock
{
    public async Task AcquireAsync(
        IEnumerable<string> resourceKeys,
        CancellationToken cancellationToken)
    {
        var keys = resourceKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
            return;

        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Order submission locks require an active database transaction.");
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
