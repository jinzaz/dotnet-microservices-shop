using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.CatalogSeeder;
using MSA.CatalogService.Domain;
using MSA.CatalogService.Infrastructure;
using MSA.Contracts;
using MSA.Grpc.Contracts.Catalog;
using MSA.SearchService;

namespace MSA.CommerceTests;

[TestClass]
public sealed class SearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly ElasticsearchOptions Options = new() { Url = "http://es.test:9200" };

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Normalize_ClampsUntrustedInput()
    {
        var request = ProductSearchQuery.Normalize(new ProductSearchRequest(
            Q: "  ｉＰｈｏｎｅ​  手机 " + new string('x', 200),
            MinPrice: 500,
            MaxPrice: 100,
            Sort: "DROP TABLE",
            Skip: 50_000,
            Take: 1_000));

        Assert.StartsWith("iPhone 手机", request.Query);
        Assert.AreEqual(ProductSearchQuery.MaxQueryLength, request.Query.Length);
        Assert.AreEqual(ProductSearchQuery.MaxTake, request.Take);
        Assert.AreEqual(ProductSearchQuery.MaxWindow - ProductSearchQuery.MaxTake, request.Skip);
        Assert.AreEqual(100m, request.MinPrice);
        Assert.AreEqual(500m, request.MaxPrice);
        Assert.AreEqual("relevance", request.Sort);
    }

    [TestMethod]
    public void Build_KeepsCategoryInPostFilterAndNeverUsesQueryString()
    {
        var categoryId = Guid.NewGuid();
        var query = ProductSearchQuery.Build(
            ProductSearchQuery.Normalize(new ProductSearchRequest(Q: "降噪 耳机 *a*", CategoryId: categoryId, MinPrice: 100)),
            Options);
        var json = query.ToJsonString();

        Assert.DoesNotContain("query_string", json);
        Assert.AreEqual(categoryId.ToString(), query["post_filter"]!["term"]!["categoryId"]!.ToString());
        Assert.DoesNotContain(categoryId.ToString(), query["query"]!.ToJsonString());
        Assert.AreEqual("now/m", query["query"]!["bool"]!["filter"]![0]!["range"]!["effectiveOnUtc"]!["lt"]!.ToString());
        Assert.AreEqual(100m, query["query"]!["bool"]!["filter"]![1]!["range"]!["maxPrice"]!["gte"]!.GetValue<decimal>());
        Assert.IsNotNull(query["highlight"]);
        Assert.IsNotNull(query["sort"]![0]!["_score"]);
        var sort = query["sort"]!.AsArray();
        Assert.IsNotNull(sort[sort.Count - 1]!["id"]);
    }

    [TestMethod]
    public void Build_EachFacetIgnoresOnlyItsOwnSelection()
    {
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var query = ProductSearchQuery.Build(
            ProductSearchQuery.Normalize(new ProductSearchRequest(CategoryId: categoryId, BrandId: brandId)),
            Options);

        // 结果同时按分类和品牌收窄；分类分面只套品牌条件，品牌分面只套分类条件。
        var postFilters = query["post_filter"]!["bool"]!["filter"]!.ToJsonString();
        Assert.Contains(categoryId.ToString(), postFilters);
        Assert.Contains(brandId.ToString(), postFilters);
        Assert.AreEqual(brandId.ToString(), query["aggs"]!["categories"]!["filter"]!["term"]!["brandId"]!.ToString());
        Assert.AreEqual(categoryId.ToString(), query["aggs"]!["brands"]!["filter"]!["term"]!["categoryId"]!.ToString());
        Assert.AreEqual("brandId", query["aggs"]!["brands"]!["aggs"]!["ids"]!["terms"]!["field"]!.ToString());
    }

    [TestMethod]
    public void ToDocument_IndexesScheduledProductsButNotUnsearchableOnes()
    {
        var product = SearchProduct(searchable: true, effectiveOn: Now.AddDays(3));

        var document = ProductSearchIndex.ToDocument(product)!;

        Assert.AreEqual(Now.AddDays(3), document.EffectiveOnUtc);
        Assert.AreEqual(99m, document.MinPrice);
        Assert.AreEqual(129m, document.MaxPrice);
        Assert.AreEqual("SKU-A", document.DefaultSku);
        CollectionAssert.AreEqual(new[] { "6900000000017" }, document.Barcodes);
        Assert.Contains("曜石黑", document.Attributes);
        Assert.AreEqual("声阔", document.BrandName);
        Assert.IsNotNull(document.BrandId);
        Assert.IsNull(ProductSearchIndex.ToDocument(SearchProduct(searchable: false, effectiveOn: Now)));
    }

    [TestMethod]
    public void BuildBulk_UsesExternalVersionsAndDeletesMissingProducts()
    {
        var present = SearchProduct(searchable: true, effectiveOn: Now);
        var missingId = Guid.NewGuid();

        var lines = ProductIndexWriter.BuildBulk([(Guid.Parse(present.Id), present), (missingId, null)])
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!)
            .ToArray();

        Assert.HasCount(3, lines);
        Assert.AreEqual("external_gte", lines[0]["index"]!["version_type"]!.ToString());
        Assert.AreEqual(7L, lines[0]["index"]!["version"]!.GetValue<long>());
        Assert.AreEqual(missingId.ToString(), lines[2]["delete"]!["_id"]!.ToString());
        Assert.IsNull(lines[2]["delete"]!["version"]);
    }

    [TestMethod]
    public void EnsureSucceeded_RetriesTransientFailuresButSkipsPoisonDocuments()
    {
        var writer = Writer();
        static JsonNode Response(params string[] items) =>
            JsonNode.Parse($$"""{"errors":true,"items":[{{string.Join(',', items)}}]}""")!;

        // 409 = 已有更新版本，404 删除 = 本来就不在，400 = 坏文档：都不阻塞。
        Assert.IsTrue(writer.EnsureSucceeded(Response(
            """{"index":{"_id":"a","status":409}}""",
            """{"delete":{"_id":"b","status":404}}""",
            """{"index":{"_id":"c","status":400,"error":{"type":"mapper_parsing_exception","reason":"bad"}}}"""), missingIndexIsFine: false));
        Assert.ThrowsExactly<ElasticsearchException>(() =>
            writer.EnsureSucceeded(Response("""{"index":{"_id":"a","status":429}}"""), missingIndexIsFine: false));
        var missing = Response("""{"index":{"_id":"a","status":404,"error":{"type":"index_not_found_exception"}}}""");
        Assert.ThrowsExactly<ElasticsearchException>(() => writer.EnsureSucceeded(missing, missingIndexIsFine: false));
        Assert.IsFalse(writer.EnsureSucceeded(missing, missingIndexIsFine: true));
    }

    [TestMethod]
    public void Parse_MapsHitsHighlightsAndFacets()
    {
        var id = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var body = JsonNode.Parse($$"""
            {
              "hits": {
                "total": { "value": 10000, "relation": "gte" },
                "hits": [{
                  "_source": { "id": "{{id}}", "categoryId": "{{categoryId}}", "name": "降噪耳机", "description": "d",
                    "defaultSku": "SKU-A", "defaultPrice": 99.0, "minPrice": 99.0, "maxPrice": 129.0, "currency": "CNY",
                    "variantCount": 2, "image": "https://img/1.svg", "updatedOnUtc": "2026-10-01T00:00:00+00:00",
                    "brandId": "{{brandId}}", "brandName": "声阔" },
                  "highlight": { "name": ["\u0002降噪\u0003耳机"] }
                }]
              },
              "aggregations": {
                "categories": { "doc_count": 42, "ids": { "buckets": [{ "key": "{{categoryId}}", "doc_count": 42 }] } },
                "brands": { "doc_count": 7, "ids": { "buckets": [{ "key": "{{brandId}}", "doc_count": 7 }] } },
                "price": { "range": { "min": 99.0, "max": 129.00000001 } }
              }
            }
            """)!;

        var result = ProductSearchService.Parse(body, ProductSearchQuery.Normalize(new ProductSearchRequest()));

        Assert.IsTrue(result.TotalIsLowerBound);
        Assert.AreEqual("\u0002降噪\u0003耳机", result.Items[0].NameHighlight);
        Assert.AreEqual(2, result.Items[0].VariantCount);
        Assert.AreEqual(42, result.Facets!.Categories[0].Count);
        Assert.AreEqual(129m, result.Facets.MaxPrice);
        Assert.AreEqual(brandId, result.Items[0].BrandId);
        Assert.AreEqual("声阔", result.Items[0].BrandName);
        Assert.AreEqual(7, result.Facets.Brands[0].Count);
    }

    [TestMethod]
    public async Task CatalogSave_WritesSearchInvalidationToTheSameOutbox()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new SearchInvalidationInterceptor())
            .Options;
        var category = new Category(Guid.NewGuid(), "AUDIO", "耳机", "operator", Now);
        var product = new Product(Guid.NewGuid(), category.Id, "SKU-1", "降噪耳机", "d", 99m, "CNY", "operator", Now);
        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        var brand = new Brand(Guid.NewGuid(), "SONIC", "声阔", "operator", Now);
        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            db.Brands.Add(brand);
            var loaded = await db.Categories.SingleAsync(TestContext.CancellationToken);
            loaded.Rename("蓝牙耳机", "operator", Now.AddMinutes(1));
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            var loaded = await db.Brands.SingleAsync(TestContext.CancellationToken);
            loaded.Rename("声阔 Sonic", "operator", Now.AddMinutes(2));
            await db.SaveChangesAsync(TestContext.CancellationToken);
        }

        await using (var db = new CatalogDbContext(options, Mock.Of<IMediator>()))
        {
            var events = (await db.OutboxMessages.ToListAsync(TestContext.CancellationToken))
                .Where(message => message.EventType.StartsWith(typeof(CatalogSearchInvalidatedIntegrationEvent).FullName!, StringComparison.Ordinal))
                .Select(message => JsonSerializer.Deserialize<CatalogSearchInvalidatedIntegrationEvent>(message.Payload)!)
                .ToArray();
            Assert.IsTrue(events.Any(value => value.ProductIds.Contains(product.Id)));
            Assert.IsTrue(events.Any(value => value.CategoryIds.Contains(category.Id)));
            Assert.IsTrue(events.Any(value => value.BrandIds.Contains(brand.Id)));
        }
    }

    [TestMethod]
    public void Seeder_GeneratesConsistentCatalogRows()
    {
        var categories = Taxonomy.All.Select(spec => new SeedCategory(Guid.NewGuid(), spec)).ToArray();
        var images = Taxonomy.All.ToDictionary(spec => spec.Code, spec => new[] { "a", "b", "c", "d", "e" });
        var brands = Taxonomy.BrandNames.ToDictionary(name => name, _ => Guid.NewGuid());
        var brandNames = brands.ToDictionary(pair => pair.Value, pair => pair.Key);
        var generator = new ProductGenerator(categories, brands, images, seed: 7, Now);
        var products = Enumerable.Range(1, 3_000).Select(sequence => generator.Next(sequence)).ToArray();

        // 商品名不重复：重名会让后台按名字查到的商品和前台看到的不是同一个。
        Assert.HasCount(products.Length, products.Select(product => product.Name).Distinct());
        var skus = products.SelectMany(product => product.Variants).Select(variant => variant.Sku).ToArray();
        Assert.HasCount(skus.Length, skus.Distinct());
        var barcodes = products.SelectMany(product => product.Variants).Select(variant => variant.Barcode).OfType<string>().ToArray();
        Assert.HasCount(barcodes.Length, barcodes.Distinct());
        Assert.IsTrue(barcodes.All(code => code.Length == 13 && code[0] == '2'));
        foreach (var product in products)
        {
            Assert.IsFalse(product.Name.Contains('{') || product.Name.Contains("  "), product.Name);
            Assert.IsInRange(1, 12, product.Variants.Count);
            Assert.AreEqual(1, product.Variants.Count(variant => variant.IsDefault));
            Assert.IsTrue(product.Variants.Single(variant => variant.IsDefault).IsActive);
            Assert.IsInRange(1, 4, product.Images.Count);
            Assert.IsTrue(product.UpdatedOnUtc >= product.CreatedOnUtc);
            // 商品挂的品牌就是名字开头的那个品牌，名字和品牌不会对不上。
            Assert.IsNotNull(product.BrandId);
            Assert.StartsWith(brandNames[product.BrandId.Value] + " ", product.Name);
            // 与目录表的检查约束一致：在售 ⇔ 已发布且有生效时间。
            Assert.AreEqual(product.LifecycleStatus == 1, product.IsActive);
            if (product.IsActive)
                Assert.IsNotNull(product.EffectiveOnUtc);
        }

        Assert.IsGreaterThan(600, products.Count(product => product.Variants.Count == 1), "single-SKU products should be common");
        Assert.IsGreaterThan(600, products.Count(product => product.Variants.Count >= 4), "multi-SKU products should be common");

        // 库存：不能为负；缺货、紧张、充足三档都要有，否则前台「无货」「仅剩 N 件」这些状态测不到。
        var stock = products.SelectMany(product => product.Variants).Select(variant => variant.Stock).ToArray();
        Assert.IsTrue(stock.All(quantity => quantity >= 0));
        Assert.IsGreaterThan(0.04, stock.Count(quantity => quantity == 0) / (double)stock.Length);
        Assert.IsGreaterThan(0.05, stock.Count(quantity => quantity is > 0 and < 10) / (double)stock.Length);
        Assert.IsGreaterThan(0.5, stock.Count(quantity => quantity >= 10) / (double)stock.Length);
    }

    [TestMethod]
    public void Barcode_HasValidEan13CheckDigit()
    {
        // 2000000000015 → 校验位按 EAN-13 规则（奇数位 ×1、偶数位 ×3）。
        Assert.AreEqual("2000000000015", ProductGenerator.Barcode(1));
        Assert.AreEqual(39.9m, ProductGenerator.NicePrice(39.4m));
        Assert.AreEqual(299m, ProductGenerator.NicePrice(293m));
        Assert.AreEqual(3499m, ProductGenerator.NicePrice(3455m));
    }

    private static SearchProduct SearchProduct(bool searchable, DateTimeOffset effectiveOn)
    {
        var product = new SearchProduct
        {
            Id = Guid.NewGuid().ToString(),
            CategoryId = Guid.NewGuid().ToString(),
            CategoryName = "耳机",
            Name = "声阔 降噪耳机",
            Description = "描述",
            Version = 7,
            Searchable = searchable,
            EffectiveOnUtc = effectiveOn.ToString("O"),
            UpdatedOnUtc = Now.ToString("O"),
            BrandId = Guid.NewGuid().ToString(),
            BrandName = "声阔"
        };
        product.Variants.Add(new SearchProductVariant
        {
            Sku = "SKU-A", NormalizedSku = "SKU-A", DisplayName = "曜石黑", AttributesJson = """{"颜色":"曜石黑"}""",
            Barcode = "6900000000017", Price = "99", Currency = "CNY", IsDefault = true
        });
        product.Variants.Add(new SearchProductVariant
        {
            Sku = "SKU-B", NormalizedSku = "SKU-B", DisplayName = "冰川白", AttributesJson = """{"颜色":"冰川白"}""",
            Barcode = "", Price = "129", Currency = "CNY"
        });
        return product;
    }

    private static ProductIndexWriter Writer()
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var elastic = new ElasticsearchHttp(Mock.Of<IHttpClientFactory>());
        return new ProductIndexWriter(
            elastic,
            new CatalogSearchFeed.CatalogSearchFeedClient(Mock.Of<CallInvoker>()),
            new BuildingAliasCache(elastic, options, TimeProvider.System),
            options,
            NullLogger<ProductIndexWriter>.Instance);
    }
}
