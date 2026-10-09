using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.SearchService;

/// <summary>
/// 把商品写进索引。所有写入都带外部版本号（目录商品的乐观并发版本，external_gte）：
/// 生产问题 #2（乱序与并发写）——消息可能乱序、重复，多个副本、多条消费线程可能同时处理同一商品，
/// 先读到的旧快照后写到也会被 ES 拒掉（409），索引里永远是版本最新的那份。
/// </summary>
internal sealed class ProductIndexWriter(
    ElasticsearchHttp elastic,
    CatalogSearchFeed.CatalogSearchFeedClient catalog,
    BuildingAliasCache building,
    IOptions<ElasticsearchOptions> options,
    ILogger<ProductIndexWriter> logger)
{
    /// <summary>重建期间新索引挂着这个别名；增量写入发现它存在时两边都写（生产问题 #4）。</summary>
    public static string BuildingAlias(ElasticsearchOptions options) => $"{options.Alias}-building";

    private ElasticsearchOptions Options => options.Value;

    /// <summary>按 id 回读目录最新快照并写入；目录里已经没有、或不再可搜的商品删除文档。</summary>
    public async Task RefreshAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        foreach (var chunk in productIds.Distinct().Chunk(500))
        {
            var request = new GetSearchProductsRequest();
            request.ProductIds.AddRange(chunk.Select(id => id.ToString()));
            var response = await catalog.GetProductsAsync(request, cancellationToken: cancellationToken);
            var found = response.Products.ToDictionary(product => Guid.Parse(product.Id));
            await WriteAsync(chunk.Select(id => (id, found.GetValueOrDefault(id))).ToArray(), cancellationToken);
        }
    }

    /// <summary>分类改名：把这个分类下可搜的商品按页从目录导出、重写一遍（文档里冗余了分类名）。</summary>
    public async Task RefreshCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var after = string.Empty;
        do
        {
            var page = await catalog.ExportProductsAsync(new ExportSearchProductsRequest
            {
                AfterId = after,
                PageSize = Options.ExportPageSize,
                CategoryId = categoryId.ToString()
            }, cancellationToken: cancellationToken);
            await WriteAsync(page.Products.Select(product => (Guid.Parse(product.Id), (SearchProduct?)product)).ToArray(), cancellationToken);
            after = page.NextAfterId;
        } while (after.Length > 0);
    }

    /// <summary>品牌改名：把这个品牌下可搜的商品按页导出、重写一遍（文档里冗余了品牌名）。</summary>
    public async Task RefreshBrandAsync(Guid brandId, CancellationToken cancellationToken)
    {
        var after = string.Empty;
        do
        {
            var page = await catalog.ExportProductsAsync(new ExportSearchProductsRequest
            {
                AfterId = after,
                PageSize = Options.ExportPageSize,
                BrandId = brandId.ToString()
            }, cancellationToken: cancellationToken);
            await WriteAsync(page.Products.Select(product => (Guid.Parse(product.Id), (SearchProduct?)product)).ToArray(), cancellationToken);
            after = page.NextAfterId;
        } while (after.Length > 0);
    }

    private async Task WriteAsync(IReadOnlyCollection<(Guid Id, SearchProduct? Product)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;
        var bulk = BuildBulk(items);
        // 生产问题 #19：require_alias 让写入只能落到别名上。别名万一不存在（首次重建还没完成、被人误删），
        // ES 默认会按文档自动建一个同名的「动态映射」索引——字段类型全靠猜、没有 IK 分词，而且之后别名再也建不出来。
        var response = await elastic.BulkAsync($"{Options.Alias}/_bulk?require_alias=true", bulk, cancellationToken);
        EnsureSucceeded(response, missingIndexIsFine: false);

        if (await building.ExistsAsync(cancellationToken))
        {
            var buildingResponse = await elastic.BulkAsync($"{BuildingAlias(Options)}/_bulk?require_alias=true", bulk, cancellationToken);
            // 重建恰好在这一刻完成并摘掉了 building 别名：新索引已经是主别名，上面那次写入已经落进去了。
            if (!EnsureSucceeded(buildingResponse, missingIndexIsFine: true))
                building.Forget();
        }
    }

    /// <summary>
    /// 一行 action、一行文档的 NDJSON。不可搜的商品写 delete——删除不存在的文档是无害的，
    /// 所以不必知道它以前在不在索引里。
    /// </summary>
    internal static string BuildBulk(IEnumerable<(Guid Id, SearchProduct? Product)> items)
    {
        var builder = new StringBuilder();
        foreach (var (id, product) in items)
        {
            var document = product is null ? null : ProductSearchIndex.ToDocument(product);
            var meta = new JsonObject { ["_id"] = id.ToString() };
            if (product is not null)
            {
                meta["version"] = product.Version;
                meta["version_type"] = "external_gte";
            }

            builder.Append(new JsonObject { [document is null ? "delete" : "index"] = meta }.ToJsonString()).Append('\n');
            if (document is not null)
                builder.Append(JsonSerializer.Serialize(document, ElasticsearchHttp.Json)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 生产问题 #18（bulk 部分失败）：bulk 的 HTTP 状态是 200 也可能有单条失败，必须逐条看。
    /// 409 = 索引里已有更新的版本、删除时 404 = 本来就不在，都算成功；429 / 5xx 抛出，整条消息稍后重试；
    /// 其余 4xx 是这条文档本身有问题（重试也不会好），记错误日志后跳过，不让一条坏数据堵死整个队列。
    /// </summary>
    /// <returns>false 表示目标索引不存在（仅在 <paramref name="missingIndexIsFine"/> 时返回，否则抛出）。</returns>
    internal bool EnsureSucceeded(JsonNode response, bool missingIndexIsFine)
    {
        if (response["errors"]?.GetValue<bool>() != true)
            return true;

        var retryable = 0;
        var missingIndex = 0;
        var staleSchema = 0;
        foreach (var item in response["items"]?.AsArray() ?? [])
        {
            if (item is not JsonObject wrapper || wrapper.FirstOrDefault() is not { Key: var action, Value: JsonObject result })
                continue;
            var status = result["status"]?.GetValue<int>() ?? 0;
            var errorType = result["error"]?["type"]?.ToString();
            if (status is >= 200 and < 300 or 409)
                continue;
            if (status == 404 && errorType is "index_not_found_exception")
            {
                missingIndex++;
                continue;
            }

            if (status == 404 && action == "delete")
                continue;
            // 生产问题 #20：映射升级期间旧索引还不认识新字段。新索引经 building 别名照样收到了这次写入，切换后旧索引就删了。
            if (errorType is "strict_dynamic_mapping_exception")
            {
                staleSchema++;
                continue;
            }
            if (status is 429 or >= 500)
            {
                retryable++;
                continue;
            }

            logger.LogError("Search document {ProductId} was rejected ({Status} {Type}): {Reason}",
                result["_id"]?.ToString(), status, errorType, result["error"]?["reason"]?.ToString());
        }

        if (staleSchema > 0)
            logger.LogWarning("{Count} documents were rejected by an index with an older schema; they land in the rebuilt index.", staleSchema);
        if (retryable > 0)
            throw new ElasticsearchException(HttpStatusCode.TooManyRequests, $"{retryable} bulk items need a retry.");
        if (missingIndex > 0 && !missingIndexIsFine)
            throw new ElasticsearchException(HttpStatusCode.NotFound,
                $"Search alias '{Options.Alias}' does not exist yet; the index is still being built.");
        return missingIndex == 0;
    }
}

/// <summary>
/// 「重建中的新索引在不在」的进程内缓存，免得每批写入都多问一次 ES。
/// 重建在挂上 building 别名后要等 <see cref="Duration"/> 的两倍再开始导出，保证所有副本都已看到它（见 IndexRebuilder）。
/// </summary>
internal sealed class BuildingAliasCache(ElasticsearchHttp elastic, IOptions<ElasticsearchOptions> options, TimeProvider clock)
{
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private (bool Exists, DateTimeOffset CheckedOnUtc) _state;

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (clock.GetUtcNow() - _state.CheckedOnUtc < Duration)
                return _state.Exists;
        }

        var alias = ProductIndexWriter.BuildingAlias(options.Value);
        var (status, _) = await elastic.SendAsync(HttpMethod.Head, $"_alias/{alias}", null, cancellationToken, HttpStatusCode.NotFound);
        var exists = status == HttpStatusCode.OK;
        lock (_gate)
            _state = (exists, clock.GetUtcNow());
        return exists;
    }

    public void Forget()
    {
        lock (_gate)
            _state = default;
    }
}
