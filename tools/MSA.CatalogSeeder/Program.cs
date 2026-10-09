using System.Diagnostics;
using System.Threading.Channels;
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
    var fakeBrands = Taxonomy.BrandNames.ToDictionary(name => name, _ => Guid.NewGuid());
    var sample = new ProductGenerator(fakeCategories, fakeBrands, fakeImages, options.Seed, now);
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

await using var dataSource = Open(connectionString);
var writer = new CatalogWriter(dataSource);

// 库存、购物车、订单库与目录库同一个 PostgreSQL 实例、不同库名（各服务 appsettings 里的 Database=）。
await using var inventoryDataSource = Open(options.InventoryConnectionString ?? Sibling("InventoryDb"));
await using var basketDataSource = Open(Sibling("BasketDb"));
await using var orderDataSource = Open(Sibling("OrderDb"));
var inventory = options.Inventory ? new InventoryWriter(inventoryDataSource) : null;
// 购物车加购、订单报价与下单都只认本地商品投影；不写的话导入的商品前台能看到、却加不进购物车。
var projections = new ProjectionWriter(basketDataSource, orderDataSource);

if (options.Reset)
{
    Console.WriteLine("删除上次导入的商品…");
    var removed = await writer.ResetAsync(token);
    Console.WriteLine($"  已删除 {removed:N0} 个商品，并通知搜索服务删除对应文档。");
    if (inventory is not null)
        Console.WriteLine($"  已删除 {await inventory.ResetAsync(token):N0} 条导入的库存（含流水）。");
    Console.WriteLine($"  已删除 {await projections.ResetAsync(token):N0} 条购物车 / 订单商品投影。");
}
else if (await writer.HasSeedDataAsync(token)
         || await projections.HasSeedDataAsync(token)
         || (inventory is not null && await inventory.HasSeedDataAsync(token)))
{
    Console.Error.WriteLine("目录、库存或购物车 / 订单投影里已经有上次导入的数据。加 --reset 先删除再导入（SKU 唯一，不能重复导入）。");
    return 1;
}

if (options.Products == 0)
    return 0;

Console.WriteLine($"准备图片池（{options.ImageSource}，每个品类 {options.ImagesPerCategory} 张）…");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var images = await new ImagePool(options, http).PrepareAsync(Taxonomy.All, token);

var categories = await writer.EnsureCategoriesAsync(Taxonomy.All, now, token);
var brands = await writer.EnsureBrandsAsync(Taxonomy.BrandNames, now, token);
Console.WriteLine($"分类 {categories.Count} 个、品牌 {brands.Count} 个就绪。开始导入 {options.Products:N0} 个商品…");

// 生成是单线程的（同一个随机种子生成同一批数据，必须按顺序），每秒两万个以上，不是瓶颈；
// 写库才是。所以生成一批就放进队列，由 --parallel 个写入任务同时 COPY，每批里目录、库存、两份投影四个库也同时写。
var generator = new ProductGenerator(categories, brands, images, options.Seed, now);
var stopwatch = Stopwatch.StartNew();
long variants = 0;
long units = 0;
long done = 0;
var timings = new StageTimings();
using var failure = CancellationTokenSource.CreateLinkedTokenSource(token);
var queue = Channel.CreateBounded<List<SeedProduct>>(options.Parallelism * 2);
var writers = Enumerable.Range(0, options.Parallelism).Select(_ => Task.Run(async () =>
{
    try
    {
        await foreach (var batch in queue.Reader.ReadAllAsync(failure.Token))
        {
            await Task.WhenAll(
                timings.Measure("目录", () => writer.WriteAsync(batch, failure.Token)),
                inventory is null ? Task.CompletedTask : timings.Measure("库存", () => inventory.WriteAsync(batch, failure.Token)),
                timings.Measure("投影", () => projections.WriteAsync(batch, failure.Token)));
            Interlocked.Add(ref variants, batch.Sum(product => product.Variants.Count));
            Interlocked.Add(ref units, batch.Sum(product => product.Variants.Sum(variant => (long)variant.Stock)));
            var total = Interlocked.Add(ref done, batch.Count);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {total,9:N0} / {options.Products:N0} 商品，{Interlocked.Read(ref variants),10:N0} 个规格，{total / stopwatch.Elapsed.TotalSeconds,7:N0} 商品/秒"));
        }
    }
    catch
    {
        // 一个写入任务失败就让生成和其他写入都停下来，别继续往数据库里灌半截数据。
        await failure.CancelAsync();
        throw;
    }
}, CancellationToken.None)).ToArray();

try
{
    for (var start = 1; start <= options.Products; start += options.BatchSize)
    {
        var count = Math.Min(options.BatchSize, options.Products - start + 1);
        var batch = new List<SeedProduct>(count);
        for (var sequence = start; sequence < start + count; sequence++)
            batch.Add(generator.Next(sequence));
        await queue.Writer.WriteAsync(batch, failure.Token);
    }
}
catch (OperationCanceledException) when (failure.IsCancellationRequested && !token.IsCancellationRequested)
{
    // 写入任务出错了：真正的异常在下面 await writers 时抛出。
}
finally
{
    queue.Writer.TryComplete();
}

await Task.WhenAll(writers);
Console.WriteLine(timings.Describe());

Console.WriteLine("更新统计信息（ANALYZE）…");
await writer.AnalyzeAsync(token);
if (inventory is not null)
    await inventory.AnalyzeAsync(token);
await projections.AnalyzeAsync(token);
var stockSummary = inventory is null ? string.Empty : $"、库存合计 {units:N0} 件";
Console.WriteLine($"完成：{options.Products:N0} 个商品、{variants:N0} 个规格{stockSummary}，用时 {stopwatch.Elapsed:mm\\:ss}。");
Console.WriteLine("搜索索引：目录服务的 Outbox 会把变更投递给搜索服务；索引不存在时搜索服务启动后会自动全量重建。");
return 0;

// synchronous_commit=off：提交时不等 WAL 落盘就返回，写入快很多。造数的数据随时能用 --reset 重来，
// 数据库崩溃时最多丢最后几百毫秒的提交，不会损坏数据。只对造数工具自己的连接生效。
NpgsqlDataSource Open(string connection) =>
    new NpgsqlDataSourceBuilder(connection)
    {
        ConnectionStringBuilder =
        {
            CommandTimeout = 300,
            Options = "-c synchronous_commit=off",
            // 并行写入时每个写入任务同时占用 4 个库各一个连接。
            MaxPoolSize = Math.Max(20, options.Parallelism * 2)
        }
    }.Build();

string Sibling(string database) =>
    new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
