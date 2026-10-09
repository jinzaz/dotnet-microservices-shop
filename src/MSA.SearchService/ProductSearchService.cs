using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace MSA.SearchService;

/// <summary>
/// 生产问题 #17（ES 故障时的雪崩）：ES 一出错就「熔断」一段时间，期间直接告诉调用方不可用（503），
/// 不再让每个请求都去等超时——否则 ES 抖一下，线程池被等待中的请求占满，连带整个服务不可用；
/// ES 恢复期也不会被积压的重试一下打死。前台收到 503 回退到目录服务的数据库搜索。
/// </summary>
internal sealed class SearchCircuitBreaker(TimeProvider clock)
{
    public static readonly TimeSpan OpenFor = TimeSpan.FromSeconds(30);

    private long _openUntilTicks;

    public bool IsOpen => clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _openUntilTicks);

    /// <returns>true 表示这次是从闭合变为打开（只在这时记日志，避免刷屏）。</returns>
    public bool Trip()
    {
        var wasOpen = IsOpen;
        Interlocked.Exchange(ref _openUntilTicks, clock.GetUtcNow().Add(OpenFor).UtcTicks);
        return !wasOpen;
    }
}

internal sealed class ProductSearchService(
    ElasticsearchHttp elastic,
    SearchCircuitBreaker circuit,
    IOptions<ElasticsearchOptions> options,
    ILogger<ProductSearchService> logger)
{
    private ElasticsearchOptions Options => options.Value;

    /// <returns>null 表示搜索引擎暂不可用。</returns>
    public async Task<ProductSearchResultDto?> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var normalized = ProductSearchQuery.Normalize(request);
        var body = await QueryAsync(ProductSearchQuery.Build(normalized, Options), Options.SearchTimeout, cancellationToken);
        return body is null ? null : Parse(body, normalized);
    }

    /// <summary>联想是锦上添花：任何失败都返回空列表，不影响页面。</summary>
    public async Task<IReadOnlyList<string>> SuggestAsync(string? prefix, CancellationToken cancellationToken)
    {
        var text = ProductSearchIndex.Normalize(prefix);
        if (text.Length is 0 or > ProductSearchQuery.MaxQueryLength)
            return [];
        var body = await QueryAsync(ProductSearchQuery.Suggest(text), TimeSpan.FromMilliseconds(300), cancellationToken);
        return body?["hits"]?["hits"]?.AsArray()
                   .Select(hit => hit?["_source"]?["name"]?.ToString())
                   .OfType<string>()
                   .ToArray()
               ?? [];
    }

    private async Task<JsonNode?> QueryAsync(JsonObject query, TimeSpan serverTimeout, CancellationToken cancellationToken)
    {
        if (circuit.IsOpen)
            return null;

        // 客户端超时比 ES 侧的 timeout 多留一秒：正常情况下 ES 自己先超时、返回部分结果。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(serverTimeout + TimeSpan.FromSeconds(1));
        try
        {
            var (_, body) = await elastic.SendAsync(HttpMethod.Post, $"{Options.Alias}/_search", query, timeout.Token);
            if (body?["timed_out"]?.GetValue<bool>() == true)
                logger.LogWarning("Search timed out on the Elasticsearch side; returning partial results.");
            return body;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                                          && exception is HttpRequestException or ElasticsearchException or OperationCanceledException or JsonException)
        {
            if (circuit.Trip())
                logger.LogWarning(exception, "Elasticsearch query failed; search is unavailable for {OpenFor}.", SearchCircuitBreaker.OpenFor);
            return null;
        }
    }

    internal static ProductSearchResultDto Parse(JsonNode body, ProductSearchQuery.Normalized request)
    {
        var hits = body["hits"];
        var total = hits?["total"]?["value"]?.GetValue<long>() ?? 0;
        var isLowerBound = hits?["total"]?["relation"]?.ToString() == "gte";
        var items = (hits?["hits"]?.AsArray() ?? [])
            .Select(hit =>
            {
                var source = hit!["_source"]!;
                return new ProductSearchItemDto(
                    source["id"]!.GetValue<Guid>(),
                    source["categoryId"]!.GetValue<Guid>(),
                    source["defaultSku"]!.ToString(),
                    source["name"]!.ToString(),
                    hit["highlight"]?["name"]?[0]?.ToString(),
                    source["description"]?.ToString() ?? string.Empty,
                    source["defaultPrice"]!.GetValue<decimal>(),
                    source["minPrice"]!.GetValue<decimal>(),
                    source["maxPrice"]!.GetValue<decimal>(),
                    source["currency"]!.ToString(),
                    source["variantCount"]?.GetValue<int>() ?? 1,
                    source["image"]?.ToString(),
                    source["updatedOnUtc"]!.GetValue<DateTimeOffset>(),
                    BrandId: source["brandId"] is JsonValue brandId ? brandId.GetValue<Guid>() : null,
                    BrandName: source["brandName"]?.ToString());
            })
            .ToArray();

        var aggregations = body["aggregations"];
        var categories = Buckets(aggregations?["categories"]);
        var brands = Buckets(aggregations?["brands"]);
        var price = aggregations?["price"]?["range"];
        return new ProductSearchResultDto(
            items,
            request.Skip,
            request.Take,
            total,
            isLowerBound,
            new ProductSearchFacets(categories, Money(price?["min"]), Money(price?["max"]), brands),
            "elasticsearch");
    }

    private static SearchFacetBucket[] Buckets(JsonNode? facet) =>
        (facet?["ids"]?["buckets"]?.AsArray() ?? [])
            .Select(bucket => new SearchFacetBucket(
                Guid.Parse(bucket!["key"]!.ToString()),
                bucket["doc_count"]!.GetValue<long>()))
            .ToArray();

    /// <summary>stats 聚合在没有命中时返回 null；scaled_float 换回的 double 四舍五入到分。</summary>
    private static decimal? Money(JsonNode? value) =>
        value is null ? null : Math.Round((decimal)value.GetValue<double>(), 2);
}
