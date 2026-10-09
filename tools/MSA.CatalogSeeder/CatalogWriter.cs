using System.Text.Json;
using MSA.Contracts;
using MSA.EventBusOutbox;
using Npgsql;
using NpgsqlTypes;

namespace MSA.CatalogSeeder;

/// <summary>
/// 用 PostgreSQL 的二进制 COPY 批量写入目录库，比逐条 INSERT 快两个数量级（50 万商品 + 百万级规格约一两分钟）。
/// 每批商品、规格、图片和对应的搜索同步消息在一个事务里提交：中途失败不会留下半个商品，重跑前用 --reset 清掉已导入的部分。
/// <para>
/// 不经过目录服务的领域代码，所以也就没有审计日志、缓存失效和 Outbox 事件——缺的那一块由这里补：
/// 往目录的 Outbox 表写 <see cref="CatalogSearchInvalidatedIntegrationEvent"/>，搜索服务照常消费、建索引。
/// 购物车 / 订单的商品投影（ProductVariantChanged）不补：订单下单时经 gRPC 实时确认价格，不依赖投影。
/// </para>
/// </summary>
internal sealed class CatalogWriter(NpgsqlDataSource dataSource)
{
    public const string SeedActor = "seed:catalog";
    private const int IdsPerEvent = 500;

    public async Task<bool> HasSeedDataAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""SELECT EXISTS (SELECT 1 FROM catalog_products WHERE "CreatedBy" = $1)""");
        command.Parameters.AddWithValue(SeedActor);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>删掉上次导入的商品（规格、图片随外键级联删除），并通知搜索服务删文档。分类只删没有别的商品在用的。</summary>
    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var deleted = new List<Guid>();
        await using (var command = new NpgsqlCommand("""DELETE FROM catalog_products WHERE "CreatedBy" = $1 RETURNING "Id" """, connection, transaction))
        {
            command.CommandTimeout = 600;
            command.Parameters.AddWithValue(SeedActor);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                deleted.Add(reader.GetGuid(0));
        }

        await using (var command = new NpgsqlCommand(
                         """
                         DELETE FROM catalog_categories c
                         WHERE c."CreatedBy" = $1 AND NOT EXISTS (SELECT 1 FROM catalog_products p WHERE p."CategoryId" = c."Id")
                         """, connection, transaction))
        {
            command.Parameters.AddWithValue(SeedActor);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteSearchEventsAsync(connection, deleted, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted.Count;
    }

    /// <summary>分类按编码幂等写入：已存在（包括后台手工建的同编码分类）就沿用，不重复建。</summary>
    public async Task<IReadOnlyList<SeedCategory>> EnsureCategoriesAsync(IReadOnlyList<CategorySpec> specs, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = new List<SeedCategory>();
        foreach (var spec in specs)
        {
            await using var command = dataSource.CreateCommand(
                """
                WITH inserted AS (
                  INSERT INTO catalog_categories ("Id", "Code", "NormalizedCode", "Name", "Status", "CreatedBy", "CreatedOnUtc", "UpdatedBy", "UpdatedOnUtc", "Version")
                  VALUES ($1, $2, $3, $4, 1, $5, $6, $5, $6, 1)
                  ON CONFLICT ("NormalizedCode") DO NOTHING
                  RETURNING "Id")
                SELECT "Id" FROM inserted
                UNION ALL
                SELECT "Id" FROM catalog_categories WHERE "NormalizedCode" = $3
                LIMIT 1
                """);
            command.Parameters.AddWithValue(Guid.CreateVersion7());
            command.Parameters.AddWithValue(spec.Code);
            command.Parameters.AddWithValue(spec.Code.ToUpperInvariant());
            command.Parameters.AddWithValue(spec.Name);
            command.Parameters.AddWithValue(SeedActor);
            command.Parameters.AddWithValue(now);
            var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
            result.Add(new SeedCategory(id, spec));
        }

        return result;
    }

    public async Task WriteAsync(IReadOnlyList<SeedProduct> products, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY catalog_products ("Id", "CategoryId", "Name", "Description", "LifecycleStatus", "IsActive", "EffectiveOnUtc",
                           "PublishedOnUtc", "UnpublishedOnUtc", "ArchivedOnUtc", "Revision", "CreatedBy", "CreatedOnUtc", "UpdatedBy", "UpdatedOnUtc", "Version")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var product in products)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(product.CategoryId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(product.Name, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.Description, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(product.LifecycleStatus, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(product.IsActive, NpgsqlDbType.Boolean, cancellationToken);
                await WriteNullableAsync(importer, product.EffectiveOnUtc, cancellationToken);
                await WriteNullableAsync(importer, product.PublishedOnUtc, cancellationToken);
                await WriteNullableAsync(importer, product.UnpublishedOnUtc, cancellationToken);
                await importer.WriteNullAsync(cancellationToken);
                await importer.WriteAsync(1L, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(SeedActor, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.CreatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(SeedActor, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.UpdatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(1L, NpgsqlDbType.Bigint, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY catalog_product_variants ("Id", "ProductId", "Sku", "NormalizedSku", "DisplayName", "AttributesJson", "Barcode",
                           "Price", "Currency", "IsActive", "IsDefault", "CreatedOnUtc", "UpdatedOnUtc")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var product in products)
            {
                foreach (var variant in product.Variants)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(variant.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(variant.Sku, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(variant.Sku.Trim().ToUpperInvariant(), NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(variant.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(variant.AttributesJson, NpgsqlDbType.Jsonb, cancellationToken);
                    if (variant.Barcode is null)
                        await importer.WriteNullAsync(cancellationToken);
                    else
                        await importer.WriteAsync(variant.Barcode, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(variant.Price, NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteAsync("CNY", NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(variant.IsActive, NpgsqlDbType.Boolean, cancellationToken);
                    await importer.WriteAsync(variant.IsDefault, NpgsqlDbType.Boolean, cancellationToken);
                    await importer.WriteAsync(product.CreatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(product.UpdatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY catalog_product_media ("Id", "ProductId", "VariantId", "MediaType", "Url", "ThumbnailUrl", "AltText", "SortOrder",
                           "CreatedOnUtc", "UpdatedOnUtc")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var product in products)
            {
                for (var index = 0; index < product.Images.Count; index++)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(Guid.CreateVersion7(), NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(product.Images[index], NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteAsync(product.Name.Length > 300 ? product.Name[..300] : product.Name, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(index, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(product.CreatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(product.UpdatedOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await WriteSearchEventsAsync(connection, products.Select(product => product.Id).ToArray(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 与目录服务自己发的消息完全一样（同一个契约类型、同一个序列化方式），由目录的 Outbox 发布器投递给搜索服务。
    /// 搜索服务还没部署时消息留在 RabbitMQ 队列里；索引还不存在时搜索服务首次启动会全量重建，这些消息只是多余的重复写。
    /// </summary>
    private static async Task WriteSearchEventsAsync(NpgsqlConnection connection, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return;
        await using var importer = await connection.BeginBinaryImportAsync(
            """
            COPY catalog_outbox_messages ("Id", "EventType", "Payload", "OccurredOnUtc", "NextAttemptOnUtc", "Status", "RetryCount", "Version")
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var chunk in productIds.Chunk(IdsPerEvent))
        {
            var integrationEvent = new CatalogSearchInvalidatedIntegrationEvent { ProductIds = chunk };
            var message = OutboxMessage.Create(
                typeof(CatalogSearchInvalidatedIntegrationEvent).AssemblyQualifiedName!,
                JsonSerializer.Serialize(integrationEvent));
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(message.Id, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(message.EventType, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(message.Payload, NpgsqlDbType.Jsonb, cancellationToken);
            await importer.WriteAsync(message.OccurredOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(message.NextAttemptOnUtc, NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync((int)OutboxStatus.Pending, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(0L, NpgsqlDbType.Bigint, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    /// <summary>批量导入后统计信息是旧的，查询计划可能选错索引；ANALYZE 一次让优化器认识新数据量。</summary>
    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "ANALYZE catalog_categories, catalog_products, catalog_product_variants, catalog_product_media, catalog_outbox_messages");
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter importer, DateTimeOffset? value, CancellationToken cancellationToken) =>
        value is { } instant
            ? importer.WriteAsync(instant, NpgsqlDbType.TimestampTz, cancellationToken)
            : importer.WriteNullAsync(cancellationToken);
}
