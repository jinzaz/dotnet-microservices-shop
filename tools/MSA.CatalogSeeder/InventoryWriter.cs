using Npgsql;
using NpgsqlTypes;

namespace MSA.CatalogSeeder;

/// <summary>
/// 给导入的每个规格建库存（库存服务的 InventoryDb）：一行库存余额，有货的再加一条「入库」流水，
/// 和后台「新增库存」接口写出来的数据一样，库存页的流水、前台的可售数量都能直接用。
/// 缺货（0 件）的规格只建余额行、不建流水——流水要求数量必须有变化。
/// <para>
/// 目录库和库存库是两个数据库，没法放进一个事务：每批先写目录、再写库存。中途失败就用 --reset 两边一起清掉重来。
/// 库存服务的可售缓存 15 秒过期（查不到时 5 秒），导入后不需要手动清 Redis。
/// </para>
/// </summary>
internal sealed class InventoryWriter(NpgsqlDataSource dataSource)
{
    /// <summary>导入的 SKU 都以这个前缀开头（见 ProductGenerator），清理时按它识别，不碰后台手工建的库存。</summary>
    private const string SeedSkuPattern = "SD-%";

    public async Task<bool> HasSeedDataAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""SELECT EXISTS (SELECT 1 FROM inventory_stock_items WHERE "Sku" LIKE $1)""");
        command.Parameters.AddWithValue(SeedSkuPattern);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>
    /// 删掉导入的库存与它们的全部流水（包括之后测试下单产生的预占、扣减流水，流水对库存行有外键）。
    /// 预占单、秒杀配额只按 SKU 字符串关联、没有外键，不受影响。
    /// </summary>
    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var movements = new NpgsqlCommand("""DELETE FROM inventory_stock_movements WHERE "Sku" LIKE $1""", connection, transaction))
        {
            movements.CommandTimeout = 600;
            movements.Parameters.AddWithValue(SeedSkuPattern);
            await movements.ExecuteNonQueryAsync(cancellationToken);
        }

        int deleted;
        await using (var items = new NpgsqlCommand("""DELETE FROM inventory_stock_items WHERE "Sku" LIKE $1""", connection, transaction))
        {
            items.CommandTimeout = 600;
            items.Parameters.AddWithValue(SeedSkuPattern);
            deleted = await items.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task WriteAsync(IReadOnlyList<SeedProduct> products, CancellationToken cancellationToken)
    {
        var rows = products
            .SelectMany(product => product.Variants.Select(variant => (Id: Guid.CreateVersion7(product.CreatedOnUtc), Variant: variant, Product: product)))
            .ToArray();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var importer = await connection.BeginBinaryImportAsync(
                         """COPY inventory_stock_items ("Id", "Sku", "OnHand", "Reserved", "Version") FROM STDIN (FORMAT BINARY)""",
                         cancellationToken))
        {
            foreach (var (id, variant, _) in rows)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(variant.Sku.Trim().ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(variant.Stock, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                // 与 new StockItem(...) 一致：新建的库存行版本号从 0 开始，之后每次变动 +1。
                await importer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY inventory_stock_movements ("Id", "StockItemId", "Sku", "Type", "OnHandDelta", "ReservedDelta", "OnHandAfter",
                           "ReservedAfter", "ReferenceType", "ReferenceId", "Reason", "OperatorId", "IdempotencyKey", "RequestHash", "OccurredOnUtc")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var (id, variant, product) in rows.Where(row => row.Variant.Stock > 0))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(Guid.CreateVersion7(product.CreatedOnUtc), NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(variant.Sku.Trim().ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken); // StockAdded
                await importer.WriteAsync(variant.Stock, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(variant.Stock, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken); // ReferenceType.None
                await importer.WriteNullAsync(cancellationToken);
                await importer.WriteAsync("Initial stock imported by the catalog seeder.", NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(CatalogWriter.SeedActor, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteNullAsync(cancellationToken);
                await importer.WriteNullAsync(cancellationToken);
                await importer.WriteAsync(product.CreatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("ANALYZE inventory_stock_items, inventory_stock_movements");
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
