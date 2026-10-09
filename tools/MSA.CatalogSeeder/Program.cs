using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using MSA.CatalogSeeder;
using MSA.Common;
using Npgsql;

// 目录造数：几十个品类、50 万商品（默认）、一百多万个规格及其库存，直接 COPY 进目录库与库存库，并经目录 Outbox 通知搜索服务建索引。
// 用法见 docs/search-service.md 的「造数」一节，或 --help；先跑 --dry-run 看看生成的商品名是否合意。
var options = SeederOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(SeederOptions.Help);
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
var token = cancellation.Token;
var now = DateTimeOffset.UtcNow;

if (options.DryRun)
{
    var fakeCategories = Taxonomy.All.Select(spec => new SeedCategory(Guid.NewGuid(), spec)).ToArray();
    var fakeImages = Taxonomy.All.ToDictionary(spec => spec.Code, spec => new[] { $"/seed/{spec.Code.ToLowerInvariant()}/01.svg" });
    var sample = new ProductGenerator(fakeCategories, fakeImages, options.Seed, now);
    for (var sequence = 1; sequence <= options.DryRunCount; sequence++)
    {
        var product = sample.Next(sequence);
        var category = fakeCategories.First(item => item.Id == product.CategoryId).Spec.Name;
        Console.WriteLine($"[{category}] {product.Name}  状态={product.LifecycleStatus} 规格={product.Variants.Count}");
        foreach (var variant in product.Variants)
            Console.WriteLine($"    {variant.Sku,-24} {variant.DisplayName,-28} ¥{variant.Price,10:0.##} 库存 {variant.Stock,5} {(variant.IsActive ? "" : "停售")}");
        Console.WriteLine($"    {product.Description}");
    }

    return 0;
}

// 连接串与对象存储地址沿用服务的约定：库名在这里写死为 CatalogDb，地址口令来自仓库根目录的 msa.env（或同名环境变量）。
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Postgresql"] = "Database=CatalogDb",
        ["ObjectStorage:PublicBucket"] = options.Bucket
    })
    .AddMsaEnvironment(Directory.GetCurrentDirectory())
    .Build();
var connectionString = options.ConnectionString ?? configuration.GetConnectionString("Postgresql")!;
options.ObjectStorageEndpoint ??= configuration["ObjectStorage:Endpoint"];
options.ObjectStorageAccessKey ??= configuration["ObjectStorage:AccessKey"];
options.ObjectStorageSecretKey ??= configuration["ObjectStorage:SecretKey"];

await using var dataSource = new NpgsqlDataSourceBuilder(connectionString)
{
    ConnectionStringBuilder = { CommandTimeout = 300 }
}.Build();
var writer = new CatalogWriter(dataSource);

// 库存库与目录库同一个 PostgreSQL 实例、不同库名（服务的 appsettings 里是 Database=InventoryDb）。
var inventoryConnection = options.InventoryConnectionString
    ?? new NpgsqlConnectionStringBuilder(connectionString) { Database = "InventoryDb" }.ConnectionString;
await using var inventoryDataSource = new NpgsqlDataSourceBuilder(inventoryConnection)
{
    ConnectionStringBuilder = { CommandTimeout = 300 }
}.Build();
var inventory = options.Inventory ? new InventoryWriter(inventoryDataSource) : null;

if (options.Reset)
{
    Console.WriteLine("删除上次导入的商品…");
    var removed = await writer.ResetAsync(token);
    Console.WriteLine($"  已删除 {removed:N0} 个商品，并通知搜索服务删除对应文档。");
    if (inventory is not null)
        Console.WriteLine($"  已删除 {await inventory.ResetAsync(token):N0} 条导入的库存（含流水）。");
}
else if (await writer.HasSeedDataAsync(token) || (inventory is not null && await inventory.HasSeedDataAsync(token)))
{
    Console.Error.WriteLine("目录库或库存库里已经有上次导入的数据。加 --reset 先删除再导入（SKU 唯一，不能重复导入）。");
    return 1;
}

if (options.Products == 0)
    return 0;

Console.WriteLine($"准备图片池（{options.ImageSource}，每个品类 {options.ImagesPerCategory} 张）…");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var images = await new ImagePool(options, http).PrepareAsync(Taxonomy.All, token);

var categories = await writer.EnsureCategoriesAsync(Taxonomy.All, now, token);
Console.WriteLine($"分类 {categories.Count} 个就绪。开始导入 {options.Products:N0} 个商品…");

var generator = new ProductGenerator(categories, images, options.Seed, now);
var stopwatch = Stopwatch.StartNew();
long variants = 0;
long units = 0;
for (var start = 1; start <= options.Products; start += options.BatchSize)
{
    var count = Math.Min(options.BatchSize, options.Products - start + 1);
    var batch = new List<SeedProduct>(count);
    for (var sequence = start; sequence < start + count; sequence++)
        batch.Add(generator.Next(sequence));
    await writer.WriteAsync(batch, token);
    if (inventory is not null)
        await inventory.WriteAsync(batch, token);
    variants += batch.Sum(product => product.Variants.Count);
    units += batch.Sum(product => product.Variants.Sum(variant => (long)variant.Stock));
    var done = start + count - 1;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  {done,9:N0} / {options.Products:N0} 商品，{variants,10:N0} 个规格，{done / stopwatch.Elapsed.TotalSeconds,7:N0} 商品/秒"));
}

Console.WriteLine("更新统计信息（ANALYZE）…");
await writer.AnalyzeAsync(token);
if (inventory is not null)
    await inventory.AnalyzeAsync(token);
var stockSummary = inventory is null ? string.Empty : $"、库存合计 {units:N0} 件";
Console.WriteLine($"完成：{options.Products:N0} 个商品、{variants:N0} 个规格{stockSummary}，用时 {stopwatch.Elapsed:mm\\:ss}。");
Console.WriteLine("搜索索引：目录服务的 Outbox 会把变更投递给搜索服务；索引不存在时搜索服务启动后会自动全量重建。");
return 0;
