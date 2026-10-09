using Npgsql;
using NpgsqlTypes;

namespace MSA.CatalogSeeder;

/// <summary>
/// 购物车（BasketDb.basket_product_snapshots）与订单（OrderDb.order_product_projections）各有一份商品投影：
/// 加购、报价、下单都先查本地投影，查不到就当作「已下架或不可售」。正常情况下它们由目录发出的
/// ProductVariantChanged 事件维护，但导入时每个规格补发一条事件要一百多万条，Outbox 默认每 2 秒发 50 条，
/// 要十几个小时，所以这里直接写投影，内容与两个服务的事件处理器写出来的完全一致：
/// <list type="bullet">
/// <item>可售 = 商品在售 且 规格在售（与 OutboxCatalogSnapshotPublisher 一致）；</item>
/// <item>生效时间 = 商品生效时间，草稿没有生效时间时用创建时间（与 ProductSnapshotProjection.EffectiveOn 一致）；</item>
/// <item>修订号 = 商品 Revision（导入时都是 1），之后后台改商品发出的事件修订号更大，会正常覆盖。</item>
/// </list>
/// </summary>
internal sealed class ProjectionWriter(NpgsqlDataSource basket, NpgsqlDataSource order)
{
    private const string SeedSkuPattern = "SD-%";

    public async Task<bool> HasSeedDataAsync(CancellationToken cancellationToken) =>
        await ExistsAsync(basket, "basket_product_snapshots", cancellationToken)
        || await ExistsAsync(order, "order_product_projections", cancellationToken);

    public async Task<int> ResetAsync(CancellationToken cancellationToken) =>
        await DeleteAsync(basket, "basket_product_snapshots", cancellationToken)
        + await DeleteAsync(order, "order_product_projections", cancellationToken);

    /// <summary>两份投影在不同的库里，互不依赖，同时写。</summary>
    public Task WriteAsync(IReadOnlyList<SeedProduct> products, CancellationToken cancellationToken) =>
        Task.WhenAll(WriteBasketAsync(products, cancellationToken), WriteOrderAsync(products, cancellationToken));

    private async Task WriteBasketAsync(IReadOnlyList<SeedProduct> products, CancellationToken cancellationToken)
    {
        await using (var connection = await basket.OpenConnectionAsync(cancellationToken))
        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY basket_product_snapshots ("VariantId", "ProductId", "Sku", "ProductName", "VariantName", "AttributesJson",
                           "Price", "Currency", "IsAvailable", "SourceRevision", "EffectiveOnUtc")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var (product, variant) in Rows(products))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(variant.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(Normalize(variant.Sku), NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.Name, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(variant.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(variant.AttributesJson, NpgsqlDbType.Jsonb, cancellationToken);
                await importer.WriteAsync(variant.Price, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync("CNY", NpgsqlDbType.Char, cancellationToken);
                await importer.WriteAsync(product.IsActive && variant.IsActive, NpgsqlDbType.Boolean, cancellationToken);
                await importer.WriteAsync(1L, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(EffectiveOn(product), NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

    }

    private async Task WriteOrderAsync(IReadOnlyList<SeedProduct> products, CancellationToken cancellationToken)
    {
        await using (var connection = await order.OpenConnectionAsync(cancellationToken))
        await using (var importer = await connection.BeginBinaryImportAsync(
                         """
                         COPY order_product_projections ("VariantId", "ProductId", "CategoryId", "Sku", "ProductName", "VariantName",
                           "AttributesJson", "Price", "Currency", "IsActive", "SourceRevision", "UpdatedOnUtc")
                         FROM STDIN (FORMAT BINARY)
                         """, cancellationToken))
        {
            foreach (var (product, variant) in Rows(products))
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(variant.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(product.Id, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(product.CategoryId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(Normalize(variant.Sku), NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.Name, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(variant.DisplayName, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(variant.AttributesJson, NpgsqlDbType.Jsonb, cancellationToken);
                await importer.WriteAsync(variant.Price, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync("CNY", NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(product.IsActive && variant.IsActive, NpgsqlDbType.Boolean, cancellationToken);
                await importer.WriteAsync(1L, NpgsqlDbType.Bigint, cancellationToken);
                // 订单投影把生效时间存在 UpdatedOnUtc 里（查询条件是 UpdatedOnUtc <= now），见 OrderProductProjection。
                await importer.WriteAsync(EffectiveOn(product), NpgsqlDbType.TimestampTz, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }
    }

    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        await using (var command = basket.CreateCommand("ANALYZE basket_product_snapshots"))
            await command.ExecuteNonQueryAsync(cancellationToken);
        await using (var command = order.CreateCommand("ANALYZE order_product_projections"))
            await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IEnumerable<(SeedProduct Product, SeedVariant Variant)> Rows(IEnumerable<SeedProduct> products) =>
        products.SelectMany(product => product.Variants.Select(variant => (product, variant)));

    private static DateTimeOffset EffectiveOn(SeedProduct product) => product.EffectiveOnUtc ?? product.CreatedOnUtc;

    private static string Normalize(string sku) => sku.Trim().ToUpperInvariant();

    private static async Task<bool> ExistsAsync(NpgsqlDataSource dataSource, string table, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""SELECT EXISTS (SELECT 1 FROM {table} WHERE "Sku" LIKE $1)""");
        command.Parameters.AddWithValue(SeedSkuPattern);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<int> DeleteAsync(NpgsqlDataSource dataSource, string table, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"""DELETE FROM {table} WHERE "Sku" LIKE $1""");
        command.CommandTimeout = 600;
        command.Parameters.AddWithValue(SeedSkuPattern);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
