using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.SearchService;

/// <summary>一个 SPU 一个文档；规格只取可售的，价格存区间。库存、促销不进索引（变化太快，查询时由各自的服务给）。</summary>
internal sealed record ProductSearchDocument(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Description,
    string Attributes,
    string[] Skus,
    string[] Barcodes,
    string DefaultSku,
    decimal DefaultPrice,
    decimal MinPrice,
    decimal MaxPrice,
    string Currency,
    int VariantCount,
    string? Image,
    DateTimeOffset EffectiveOnUtc,
    DateTimeOffset UpdatedOnUtc);

internal static class ProductSearchIndex
{
    /// <summary>
    /// 生产问题 #8（高亮 XSS）：高亮标记不用 &lt;em&gt;，而用文本里不可能出现的控制字符（写入前已从文本里剔除），
    /// 前端按标记切成 React 节点渲染，永远不需要 innerHTML。商品名是后台人员输入的，不能当 HTML 信任。
    /// </summary>
    public const string HighlightStart = "\u0002";

    public const string HighlightEnd = "\u0003";

    private const int MaxDescriptionLength = 4_000;

    public static string SynonymSet(ElasticsearchOptions options) => $"{options.Alias}-synonyms";

    public static bool HasSynonyms(ElasticsearchOptions options) =>
        options.Synonyms.Any(rule => !string.IsNullOrWhiteSpace(rule));

    /// <summary>
    /// 新索引的设置与映射。建索引时副本为 0、关闭刷新，灌完数据再改回（生产问题 #13：全量导入提速数倍）。
    /// </summary>
    public static JsonObject Definition(ElasticsearchOptions options)
    {
        var searchFilters = new JsonArray("lowercase");
        var filters = new JsonObject();
        if (HasSynonyms(options))
        {
            // updateable 的同义词只能用在查询分析器上；改同义词集合后 ES 自动重载，已有索引不用重建。
            filters["product_synonyms"] = new JsonObject
            {
                ["type"] = "synonym_graph",
                ["synonyms_set"] = SynonymSet(options),
                ["updateable"] = true
            };
            searchFilters.Add("product_synonyms");
        }

        return new JsonObject
        {
            ["settings"] = new JsonObject
            {
                ["number_of_shards"] = options.Shards,
                ["number_of_replicas"] = 0,
                ["refresh_interval"] = "-1",
                // 生产问题 #5（深分页）：from + size 超过这个窗口 ES 直接报错；接口层同样封顶，不让请求打到这里。
                ["max_result_window"] = ProductSearchQuery.MaxWindow,
                ["analysis"] = new JsonObject
                {
                    ["normalizer"] = new JsonObject
                    {
                        ["sku_normalizer"] = new JsonObject
                        {
                            ["type"] = "custom",
                            ["filter"] = new JsonArray("lowercase", "asciifolding")
                        }
                    },
                    ["filter"] = filters,
                    ["analyzer"] = new JsonObject
                    {
                        ["product_index"] = new JsonObject
                        {
                            ["type"] = "custom",
                            ["tokenizer"] = options.IndexTokenizer,
                            ["filter"] = new JsonArray("lowercase")
                        },
                        ["product_search"] = new JsonObject
                        {
                            ["type"] = "custom",
                            ["tokenizer"] = options.SearchTokenizer,
                            ["filter"] = searchFilters
                        }
                    }
                }
            },
            ["mappings"] = new JsonObject
            {
                // 生产问题 #9（映射爆炸）：strict 拒绝未声明字段，写错字段名会直接报错，而不是悄悄长出一堆动态字段。
                ["dynamic"] = "strict",
                ["properties"] = new JsonObject
                {
                    // 生产问题 #7：排序的兜底键用自己的 keyword 字段，不用 _id（_id 排序要加载 fielddata，内存大户）。
                    ["id"] = Keyword(),
                    ["categoryId"] = Keyword(),
                    ["categoryName"] = Text(withKeyword: true),
                    ["name"] = Text(withKeyword: true),
                    ["description"] = Text(withKeyword: false),
                    ["attributes"] = Text(withKeyword: false),
                    ["skus"] = new JsonObject { ["type"] = "keyword", ["normalizer"] = "sku_normalizer" },
                    ["barcodes"] = Keyword(),
                    ["defaultSku"] = Stored("keyword"),
                    ["defaultPrice"] = Price(),
                    ["minPrice"] = Price(),
                    ["maxPrice"] = Price(),
                    ["currency"] = Keyword(),
                    ["variantCount"] = new JsonObject { ["type"] = "integer", ["index"] = false },
                    ["image"] = Stored("keyword"),
                    ["effectiveOnUtc"] = new JsonObject { ["type"] = "date" },
                    ["updatedOnUtc"] = new JsonObject { ["type"] = "date" }
                }
            }
        };
    }

    private static JsonObject Keyword() => new() { ["type"] = "keyword" };

    /// <summary>只展示、不查询的字段：不建倒排、不建列存，只留在 _source 里。</summary>
    private static JsonObject Stored(string type) => new() { ["type"] = type, ["index"] = false, ["doc_values"] = false };

    /// <summary>金额用 scaled_float（×100 存成整数）：比 double 省空间，也没有二进制浮点的误差。</summary>
    private static JsonObject Price() => new() { ["type"] = "scaled_float", ["scaling_factor"] = 100 };

    private static JsonObject Text(bool withKeyword)
    {
        var field = new JsonObject
        {
            ["type"] = "text",
            ["analyzer"] = "product_index",
            ["search_analyzer"] = "product_search"
        };
        if (withKeyword)
            field["fields"] = new JsonObject { ["keyword"] = new JsonObject { ["type"] = "keyword", ["ignore_above"] = 256 } };
        return field;
    }

    /// <summary>
    /// 目录判定为可搜索的商品才有文档（规则在目录里，这里不重复）。生效时间在未来的（定时上架）照样写进去，
    /// 查询时按当前时间过滤（生产问题 #6）——到点上架不会产生任何数据库写入，也就不会有同步消息。
    /// </summary>
    public static ProductSearchDocument? ToDocument(SearchProduct product)
    {
        if (!product.Searchable
            || product.Variants.Count == 0
            || !DateTimeOffset.TryParse(product.EffectiveOnUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var effectiveOn))
        {
            return null;
        }

        var variants = product.Variants;
        var defaultVariant = variants.FirstOrDefault(variant => variant.IsDefault) ?? variants[0];
        var prices = variants.Select(variant => Price(variant.Price)).ToArray();
        var attributes = variants
            .SelectMany(variant => AttributeValues(variant.AttributesJson).Append(variant.DisplayName))
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal);
        var description = Normalize(product.Description);
        DateTimeOffset.TryParse(product.UpdatedOnUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updatedOn);

        return new ProductSearchDocument(
            Guid.Parse(product.Id),
            Guid.Parse(product.CategoryId),
            Normalize(product.CategoryName),
            Normalize(product.Name),
            description.Length > MaxDescriptionLength ? description[..MaxDescriptionLength] : description,
            string.Join(' ', attributes),
            variants.Select(variant => variant.NormalizedSku).ToArray(),
            variants.Select(variant => variant.Barcode).Where(barcode => barcode.Length > 0).ToArray(),
            defaultVariant.Sku,
            Price(defaultVariant.Price),
            prices.Min(),
            prices.Max(),
            defaultVariant.Currency,
            variants.Count,
            string.IsNullOrEmpty(product.ImageUrl) ? null : product.ImageUrl,
            effectiveOn,
            updatedOn);
    }

    private static decimal Price(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static IEnumerable<string> AttributeValues(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [];
            return document.RootElement.EnumerateObject()
                .Select(property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// 生产问题 #14（全角半角、零宽字符）：写入与查询都做 NFKC 规范化——「ｉＰｈｏｎｅ」「iPhone」算同一个词；
    /// 去掉控制字符与零宽字符（复制粘贴常带进来），并把连续空白压成一个。
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var lastWasSpace = false;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                if (character is not ('\n' or '\r' or '\t'))
                    continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && builder.Length > 0)
                    builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}
