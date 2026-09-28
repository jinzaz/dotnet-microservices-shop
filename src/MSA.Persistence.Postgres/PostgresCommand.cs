using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MSA.Persistence.Postgres;

/// <summary>
/// 执行本库生成的原生命令。始终复用 EF 当前的连接与事务，这样锁、Upsert 与业务写入都在同一个事务里，
/// 不会出现「锁在另一条连接上」这种最难排查的问题。
/// </summary>
internal static class PostgresCommand
{
    public static async Task<int> ExecuteNonQueryAsync(
        DbContext context,
        string sql,
        Action<DbCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        var (command, opened) = await CreateAsync(context, sql, configureParameters, cancellationToken);
        await using (command)
        {
            try
            {
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                if (opened)
                    await context.Database.GetDbConnection().CloseAsync();
            }
        }
    }

    public static async Task<object?> ExecuteScalarAsync(
        DbContext context,
        string sql,
        Action<DbCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        var (command, opened) = await CreateAsync(context, sql, configureParameters, cancellationToken);
        await using (command)
        {
            try
            {
                return await command.ExecuteScalarAsync(cancellationToken);
            }
            finally
            {
                if (opened)
                    await context.Database.GetDbConnection().CloseAsync();
            }
        }
    }

    private static async Task<(DbCommand Command, bool OpenedConnection)> CreateAsync(
        DbContext context,
        string sql,
        Action<DbCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        configureParameters(command);
        return (command, opened);
    }
}
