using System.Globalization;

namespace MSA.CatalogSeeder;

internal sealed class SeederOptions
{
    public const string Help = """
        MSA 目录造数工具

        用法：dotnet run --project tools/MSA.CatalogSeeder -- [选项]

          --products <N>         导入的商品数，默认 500000；0 表示只执行 --reset
          --seed <N>             随机种子，同一个种子生成同一批商品，默认 20261008
          --batch <N>            每个事务写入的商品数，默认 5000
          --parallel <N>         同时写入的批数，默认 4；数据库服务器核数多、磁盘快可以调大
          --reset                先删除上次导入的商品（CreatedBy = seed:catalog）及其规格、图片
          --dry-run [N]          不连数据库，只打印 N 个（默认 20）生成的商品看看效果
          --images <svg|pexels>  图片来源：svg 本地生成占位图（默认）；pexels 下载真实照片（需要 --pexels-key）
          --pexels-key <KEY>     Pexels API key（https://www.pexels.com/api/ 免费申请），也可用环境变量 PEXELS_API_KEY
          --images-per-category <N>  每个品类的图片池大小，默认 8
          --bucket <NAME>        对象存储的公共桶，默认 shop-public（与后台上传商品图同一个桶）
          --image-dir <PATH>     不传对象存储，把图片写到本地目录（例如 src/MSA.Web/Front/public）
          --image-base-url <URL> 配合 --image-dir：图片的访问前缀，默认 /
          --connection <STRING>  目录库连接串；默认按 msa.env 拼出 Database=CatalogDb
          --inventory-connection <STRING>  库存库连接串；默认沿用目录库的地址口令、库名换成 InventoryDb
          --no-inventory         不导入库存
        """;

    public int Products { get; private set; } = 500_000;
    public int Seed { get; private set; } = 20261008;
    public int BatchSize { get; private set; } = 5_000;
    public int Parallelism { get; private set; } = 4;
    public bool Reset { get; private set; }
    public bool DryRun { get; private set; }
    public int DryRunCount { get; private set; } = 20;
    public string ImageSource { get; private set; } = "svg";
    public string? PexelsApiKey { get; private set; } = Environment.GetEnvironmentVariable("PEXELS_API_KEY");
    public int ImagesPerCategory { get; private set; } = 8;
    public string Bucket { get; private set; } = "shop-public";
    public string? ImageDirectory { get; private set; }
    public string ImageBaseUrl { get; private set; } = "/";
    public string? ConnectionString { get; private set; }
    public string? InventoryConnectionString { get; private set; }
    public bool Inventory { get; private set; } = true;
    public bool ShowHelp { get; private set; }

    public string? ObjectStorageEndpoint { get; set; }
    public string? ObjectStorageAccessKey { get; set; }
    public string? ObjectStorageSecretKey { get; set; }

    public static SeederOptions Parse(string[] args)
    {
        var options = new SeederOptions();
        for (var index = 0; index < args.Length; index++)
        {
            string Value() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} 缺少参数值。");
            int Number() => int.Parse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            switch (args[index])
            {
                case "--products": options.Products = Number(); break;
                case "--seed": options.Seed = Number(); break;
                case "--batch": options.BatchSize = Number(); break;
                case "--parallel": options.Parallelism = Number(); break;
                case "--reset": options.Reset = true; break;
                case "--dry-run":
                    options.DryRun = true;
                    if (index + 1 < args.Length && int.TryParse(args[index + 1], out var count))
                    {
                        options.DryRunCount = count;
                        index++;
                    }
                    break;
                case "--images": options.ImageSource = Value().ToLowerInvariant(); break;
                case "--pexels-key": options.PexelsApiKey = Value(); break;
                case "--images-per-category": options.ImagesPerCategory = Number(); break;
                case "--bucket": options.Bucket = Value(); break;
                case "--image-dir": options.ImageDirectory = Path.GetFullPath(Value()); break;
                case "--image-base-url": options.ImageBaseUrl = Value(); break;
                case "--connection": options.ConnectionString = Value(); break;
                case "--inventory-connection": options.InventoryConnectionString = Value(); break;
                case "--no-inventory": options.Inventory = false; break;
                case "-h" or "--help": options.ShowHelp = true; break;
                default: throw new ArgumentException($"未知参数 {args[index]}，用 --help 查看用法。");
            }
        }

        if (options.Products < 0 || options.BatchSize is < 1 or > 50_000 || options.ImagesPerCategory is < 1 or > 40
            || options.Parallelism is < 1 or > 16)
            throw new ArgumentException("--products 不能为负；--batch 取 1–50000；--parallel 取 1–16；--images-per-category 取 1–40。");
        if (options.ImageSource is not ("svg" or "pexels"))
            throw new ArgumentException("--images 只能是 svg 或 pexels。");
        if (options.ImageSource == "pexels" && string.IsNullOrWhiteSpace(options.PexelsApiKey))
            throw new ArgumentException("--images pexels 需要 --pexels-key 或环境变量 PEXELS_API_KEY。");
        return options;
    }
}
