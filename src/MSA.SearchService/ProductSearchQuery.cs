using System.Text.Json.Nodes;

namespace MSA.SearchService;

/// <summary>前台搜索参数。全部可选，绑定自查询字符串（q、categoryId、brandId、minPrice、maxPrice、sort、skip、take）。</summary>
public sealed record ProductSearchRequest(
    string? Q = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? Sort = null,
    int? Skip = null,
    int? Take = null);

public sealed record ProductSearchItemDto(
    Guid Id,
    Guid CategoryId,
    string Sku,
    string Name,
    string? NameHighlight,
    string Description,
    decimal Price,
    decimal MinPrice,
    decimal MaxPrice,
    string Currency,
    int VariantCount,
    string? Image,
    DateTimeOffset UpdatedOnUtc,
    bool IsActive = true,
    Guid? BrandId = null,
    string? BrandName = null);

public sealed record SearchFacetBucket(Guid Id, long Count);

/// <param name="Brands">命中最多的 30 个品牌；没有品牌的商品不计入。</param>
public sealed record ProductSearchFacets(
    IReadOnlyList<SearchFacetBucket> Categories,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<SearchFacetBucket> Brands);

/// <param name="TotalIsLowerBound">命中超过 1 万时只给下界（显示「10000+」），不为一个精确数字扫完全部命中。</param>
/// <param name="Engine">elasticsearch 或 database（降级）。降级时没有分面、排序与价格筛选，前端据此隐藏对应控件。</param>
public sealed record ProductSearchResultDto(
    IReadOnlyList<ProductSearchItemDto> Items,
    int Skip,
    int Take,
    long Total,
    bool TotalIsLowerBound,
    ProductSearchFacets? Facets,
    string Engine);

/// <summary>把请求翻成 ES 查询 DSL。纯函数，便于单测。</summary>
internal static class ProductSearchQuery
{
    public const int MaxWindow = 10_000;
    public const int MaxTake = 48;
    public const int MaxQueryLength = 80;

    private static readonly string[] SourceFields =
    [
        "id", "categoryId", "name", "description", "defaultSku", "defaultPrice", "minPrice", "maxPrice",
        "currency", "variantCount", "image", "updatedOnUtc", "brandId", "brandName"
    ];

    internal sealed record Normalized(
        string Query,
        Guid? CategoryId,
        Guid? BrandId,
        decimal? MinPrice,
        decimal? MaxPrice,
        string Sort,
        int Skip,
        int Take);

    /// <summary>
    /// 生产问题 #3（不可信输入）：用户输入只进 match/term 的 query 值，从不拼进 query_string——
    /// 后者支持通配符、正则和字段语法，一个 <c>*a*</c> 或畸形正则就能让集群满负荷。长度、页大小、翻页深度全部封顶。
    /// </summary>
    public static Normalized Normalize(ProductSearchRequest request)
    {
        var query = ProductSearchIndex.Normalize(request.Q);
        if (query.Length > MaxQueryLength)
            query = query[..MaxQueryLength];
        var take = Math.Clamp(request.Take ?? 20, 1, MaxTake);
        // 生产问题 #5（深分页）：from + size 不超过 1 万；再往后的「下一页」前端会因 total 封顶而自然停止。
        var skip = Math.Clamp(request.Skip ?? 0, 0, MaxWindow - take);
        decimal? min = request.MinPrice is >= 0 ? request.MinPrice : null;
        decimal? max = request.MaxPrice is >= 0 ? request.MaxPrice : null;
        if (min > max)
            (min, max) = (max, min);
        var sort = request.Sort?.Trim().ToLowerInvariant() switch
        {
            "price_asc" or "price_desc" or "newest" => request.Sort.Trim().ToLowerInvariant(),
            _ => "relevance"
        };
        return new Normalized(query, request.CategoryId, request.BrandId, min, max, sort, skip, take);
    }

    public static JsonObject Build(Normalized request, ElasticsearchOptions options)
    {
        var hasQuery = request.Query.Length > 0;
        var filters = new JsonArray
        {
            // 生产问题 #6（定时上架）：按当前时间过滤。「now/m」向下取整到分钟，同一分钟内的查询可以复用节点缓存；
            // 用 lt 而不是 lte，刚到点的商品最多晚一分钟出现，而不会提前露出。
            new JsonObject { ["range"] = new JsonObject { ["effectiveOnUtc"] = new JsonObject { ["lt"] = "now/m" } } }
        };
        if (request.MinPrice is { } min)
            filters.Add(Range("maxPrice", "gte", min));
        if (request.MaxPrice is { } max)
            filters.Add(Range("minPrice", "lte", max));

        var root = new JsonObject
        {
            ["from"] = request.Skip,
            ["size"] = request.Take,
            ["timeout"] = $"{(int)options.SearchTimeout.TotalMilliseconds}ms",
            ["track_total_hits"] = MaxWindow,
            ["_source"] = new JsonArray(SourceFields.Select(field => (JsonNode)field).ToArray()),
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["must"] = new JsonArray(hasQuery ? Relevance(request.Query) : new JsonObject { ["match_all"] = new JsonObject() }),
                    ["should"] = hasQuery
                        ? new JsonArray(new JsonObject
                        {
                            // 整句按顺序出现的排在前面（「无线降噪耳机」优先于分别含这几个词的）。
                            ["match_phrase"] = new JsonObject
                            {
                                ["name"] = new JsonObject { ["query"] = request.Query, ["slop"] = 1, ["boost"] = 3 }
                            }
                        })
                        : new JsonArray(),
                    ["filter"] = filters
                }
            },
            ["sort"] = Sort(request.Sort, hasQuery),
            // 生产问题 #15（分面计数塌缩）：分类、品牌条件放在 post_filter 而不是 query 里，每个分面只套用「别的」分面条件——
            // 选中「耳机」后分类分面仍显示各分类的命中数（但已按选中的品牌收窄），品牌分面同理；价格区间两个条件都套用。
            // 放进 query 的话，选中一个分类后其他分类都会变成 0，用户没法横向切换。
            ["aggs"] = new JsonObject
            {
                ["categories"] = Facet(Filter(BrandFilter(request.BrandId)), "categoryId", 50),
                ["brands"] = Facet(Filter(CategoryFilter(request.CategoryId)), "brandId", 30),
                ["price"] = new JsonObject
                {
                    ["filter"] = Filter(CategoryFilter(request.CategoryId), BrandFilter(request.BrandId)),
                    ["aggs"] = new JsonObject
                    {
                        ["range"] = new JsonObject { ["stats"] = new JsonObject { ["field"] = "minPrice" } }
                    }
                }
            }
        };

        if (CategoryFilter(request.CategoryId) is not null || BrandFilter(request.BrandId) is not null)
            root["post_filter"] = Filter(CategoryFilter(request.CategoryId), BrandFilter(request.BrandId));
        if (hasQuery)
        {
            root["highlight"] = new JsonObject
            {
                ["pre_tags"] = new JsonArray(ProductSearchIndex.HighlightStart),
                ["post_tags"] = new JsonArray(ProductSearchIndex.HighlightEnd),
                ["fields"] = new JsonObject { ["name"] = new JsonObject { ["number_of_fragments"] = 0 } }
            };
        }

        return root;
    }

    /// <summary>
    /// 商品名权重最高，其次分类名与规格属性，描述只做补充召回。两三个词时要求全部命中，词多时允许缺一部分，
    /// 兼顾「搜得准」和「长句也搜得到」。SKU / 条码精确命中直接置顶。
    /// </summary>
    private static JsonObject Relevance(string query) => new()
    {
        ["bool"] = new JsonObject
        {
            ["should"] = new JsonArray
            {
                new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = query,
                        ["fields"] = new JsonArray("name^4", "brandName^3", "categoryName^2", "attributes^1.5", "description^0.5"),
                        ["type"] = "best_fields",
                        ["tie_breaker"] = 0.3,
                        ["minimum_should_match"] = "2<75%"
                    }
                },
                new JsonObject
                {
                    ["term"] = new JsonObject
                    {
                        ["skus"] = new JsonObject { ["value"] = query, ["boost"] = 50 }
                    }
                },
                new JsonObject
                {
                    ["term"] = new JsonObject
                    {
                        ["barcodes"] = new JsonObject { ["value"] = query, ["boost"] = 50 }
                    }
                }
            },
            ["minimum_should_match"] = 1
        }
    };

    private static JsonArray Sort(string sort, bool hasQuery) => sort switch
    {
        "price_asc" => [Field("minPrice", "asc"), Field("id", "asc")],
        "price_desc" => [Field("maxPrice", "desc"), Field("id", "asc")],
        "newest" => [Field("effectiveOnUtc", "desc"), Field("id", "asc")],
        // 生产问题 #7（翻页抖动）：同分的文档顺序不稳定，第二页可能重复或漏掉第一页的商品；最后总用唯一的 id 兜底。
        _ when hasQuery => [Field("_score", "desc"), Field("effectiveOnUtc", "desc"), Field("id", "asc")],
        _ => [Field("effectiveOnUtc", "desc"), Field("id", "asc")]
    };

    private static JsonObject Field(string name, string order) => new() { [name] = new JsonObject { ["order"] = order } };

    private static JsonObject Range(string field, string op, decimal value) =>
        new() { ["range"] = new JsonObject { [field] = new JsonObject { [op] = value } } };

    private static JsonObject? CategoryFilter(Guid? categoryId) => categoryId is { } id
        ? new JsonObject { ["term"] = new JsonObject { ["categoryId"] = id.ToString() } }
        : null;

    private static JsonObject? BrandFilter(Guid? brandId) => brandId is { } id
        ? new JsonObject { ["term"] = new JsonObject { ["brandId"] = id.ToString() } }
        : null;

    /// <summary>把若干个（可能为空的）条件合成一个 filter；全为空时是 match_all。</summary>
    private static JsonObject Filter(params JsonObject?[] filters)
    {
        var present = filters.OfType<JsonObject>().ToArray();
        return present.Length switch
        {
            0 => new JsonObject { ["match_all"] = new JsonObject() },
            1 => present[0],
            _ => new JsonObject { ["bool"] = new JsonObject { ["filter"] = new JsonArray(present.Select(item => (JsonNode)item).ToArray()) } }
        };
    }

    private static JsonObject Facet(JsonObject filter, string field, int size) => new()
    {
        ["filter"] = filter,
        ["aggs"] = new JsonObject { ["ids"] = new JsonObject { ["terms"] = new JsonObject { ["field"] = field, ["size"] = size } } }
    };

    /// <summary>搜索框联想：商品名前缀短语匹配，按商品名去重，最多 8 条。</summary>
    public static JsonObject Suggest(string prefix) => new()
    {
        ["size"] = 8,
        ["timeout"] = "300ms",
        ["_source"] = new JsonArray("name"),
        ["query"] = new JsonObject
        {
            ["bool"] = new JsonObject
            {
                ["must"] = new JsonArray(new JsonObject
                {
                    ["match_phrase_prefix"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["query"] = prefix, ["max_expansions"] = 30 }
                    }
                }),
                ["filter"] = new JsonArray(new JsonObject
                {
                    ["range"] = new JsonObject { ["effectiveOnUtc"] = new JsonObject { ["lt"] = "now/m" } }
                })
            }
        },
        ["collapse"] = new JsonObject { ["field"] = "name.keyword" }
    };
}
