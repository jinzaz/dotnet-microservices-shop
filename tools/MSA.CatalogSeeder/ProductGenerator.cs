using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSA.CatalogSeeder;

internal sealed record SeedCategory(Guid Id, CategorySpec Spec);

internal sealed record SeedVariant(
    Guid Id,
    string Sku,
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    bool IsActive,
    bool IsDefault,
    int Stock);

internal sealed record SeedProduct(
    Guid Id,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    int LifecycleStatus,
    bool IsActive,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    DateTimeOffset? PublishedOnUtc,
    DateTimeOffset? EffectiveOnUtc,
    DateTimeOffset? UnpublishedOnUtc,
    IReadOnlyList<SeedVariant> Variants,
    IReadOnlyList<string> Images);

/// <summary>
/// 按分类权重抽品类，再按品类词表生成商品。同一个随机种子总是生成同一批数据（id 除外），方便复现问题。
/// <list type="bullet">
/// <item>规格数：约 35% 单规格，30% 2–3 个，25% 4–6 个，10% 7–12 个（由品类的销售属性组合而成）；</item>
/// <item>状态：约 92% 已发布（其中 1% 定时上架、生效时间在未来），4% 已下架，4% 草稿；</item>
/// <item>价格：在品类价格区间内按对数均匀分布（便宜货多、贵货少），按价位取整成 x9 / x99 这类常见价格；</item>
/// <item>库存：约 8% 缺货、12% 紧张（1–9 件），其余按价位分布——便宜的备货多、贵的备货少。</item>
/// </list>
/// </summary>
/// <param name="brands">品牌名 → 品牌 id。商品名里用了哪个品牌，商品就挂哪个品牌，名字和品牌不会对不上。</param>
internal sealed partial class ProductGenerator(
    IReadOnlyList<SeedCategory> categories,
    IReadOnlyDictionary<string, Guid> brands,
    IReadOnlyDictionary<string, string[]> images,
    int seed,
    DateTimeOffset now)
{
    private const int Published = 1;
    private const int Unpublished = 2;
    private const int Draft = 0;

    private readonly Random _random = new(seed);
    private readonly double[] _cumulativeWeights = Cumulative(categories);
    private long _variantSequence;
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public SeedProduct Next(long sequence)
    {
        var category = PickCategory();
        var spec = category.Spec;
        var (name, brand) = UniqueName(spec);
        var basePrice = NicePrice(LogUniform(spec.MinPrice, spec.MaxPrice));
        var variants = Variants(spec, sequence, basePrice);

        var created = now.AddDays(-_random.Next(30, 720)).AddMinutes(-_random.Next(0, 1440));
        var updated = Min(created.AddDays(_random.Next(0, 30)).AddMinutes(_random.Next(0, 1440)), now);
        var roll = _random.NextDouble();
        int status;
        bool active;
        DateTimeOffset? published = null, effective = null, unpublished = null;
        if (roll < .92)
        {
            status = Published;
            active = true;
            published = Min(created.AddDays(_random.Next(0, 10)), now);
            // 1% 定时上架：生效时间在未来，前台和搜索都不应该在到点前看到它。
            effective = _random.NextDouble() < .01 ? now.AddDays(_random.Next(1, 15)) : published;
            updated = Max(updated, published.Value);
        }
        else if (roll < .96)
        {
            status = Unpublished;
            active = false;
            published = Min(created.AddDays(_random.Next(0, 10)), now);
            unpublished = Min(published.Value.AddDays(_random.Next(1, 120)), now);
            effective = published;
            updated = Max(updated, unpublished.Value);
        }
        else
        {
            status = Draft;
            active = false;
        }

        var pool = images.GetValueOrDefault(spec.Code) ?? [];
        var imageCount = Math.Min(pool.Length, _random.NextDouble() switch { < .3 => 1, < .65 => 2, < .9 => 3, _ => 4 });
        var picked = pool.OrderBy(_ => _random.Next()).Take(imageCount).ToArray();

        // 主键用「现在」生成的 UUIDv7，按写入顺序递增：B-tree 只在最右边追加。
        // 用随机的创建时间生成的话，主键在两年的时间范围里乱序插入，每行都可能落到不同的索引页，越写越慢。
        return new SeedProduct(
            Guid.CreateVersion7(),
            category.Id,
            brand is null ? null : brands.GetValueOrDefault(brand),
            name,
            Description(spec, name),
            status,
            active,
            created,
            updated,
            published,
            effective,
            unpublished,
            variants,
            picked);
    }

    private SeedCategory PickCategory()
    {
        var roll = _random.NextDouble() * _cumulativeWeights[^1];
        var index = Array.BinarySearch(_cumulativeWeights, roll);
        return categories[index < 0 ? ~index : index];
    }

    private static double[] Cumulative(IReadOnlyList<SeedCategory> categories)
    {
        var sums = new double[categories.Count];
        double total = 0;
        for (var index = 0; index < categories.Count; index++)
            sums[index] = total += categories[index].Spec.Weight;
        return sums;
    }

    [GeneratedRegex(@"\{(?:#(?<from>\d+)-(?<to>\d+)|(?<slot>\w+))\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    /// <summary>
    /// 商品名不重复：词表组合少的品类（例如天幕只有 品牌 × 款式 16 种）随机几次仍然撞名时，像真实商品一样带上型号，
    /// 例如「野径 加厚银胶天幕 TE-4071」。否则 50 万商品里会有成百上千个同名商品，后台按名字查到的未必是前台看到的那一个。
    /// </summary>
    private (string Name, string? Brand) UniqueName(CategorySpec spec)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = Name(spec, out var candidateBrand);
            if (_names.Add(candidate))
                return (candidate, candidateBrand);
        }

        var baseName = Name(spec, out var brand);
        var prefix = spec.Code[..2];
        while (true)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName} {prefix}-{_random.Next(1000, 10000)}");
            if (_names.Add(candidate))
                return (candidate, brand);
        }
    }

    internal string Name(CategorySpec spec, out string? brand)
    {
        var template = spec.Templates[_random.Next(spec.Templates.Length)];
        string? chosenBrand = null;
        var name = Token().Replace(template, match =>
        {
            if (match.Groups["slot"].Success)
            {
                var slot = match.Groups["slot"].Value;
                var words = spec.Slots[slot];
                var word = words[_random.Next(words.Length)];
                if (slot == "brand")
                    chosenBrand = word;
                return word;
            }

            var from = int.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
            var to = int.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture);
            return _random.Next(from, to + 1).ToString(CultureInfo.InvariantCulture);
        });
        brand = chosenBrand;
        return Spaces().Replace(name, " ").Trim();
    }

    private string Description(CategorySpec spec, string name)
    {
        var highlights = spec.Highlights.OrderBy(_ => _random.Next()).Take(_random.Next(2, 4));
        var builder = new StringBuilder(name).Append("。");
        foreach (var highlight in highlights)
            builder.Append(highlight).Append('；');
        builder.Length--;
        builder.Append("。").Append(Taxonomy.Promises[_random.Next(Taxonomy.Promises.Length)]);
        return builder.ToString();
    }

    private IReadOnlyList<SeedVariant> Variants(CategorySpec spec, long sequence, decimal basePrice)
    {
        var target = _random.NextDouble() switch
        {
            < .35 => 1,
            < .65 => _random.Next(2, 4),
            < .90 => _random.Next(4, 7),
            _ => _random.Next(7, 13)
        };

        // 按目标数量给每个销售属性分配取几个值，组合后的笛卡尔积就是这件商品的全部规格。
        var chosen = new List<(AttributeSpec Attribute, int[] Indexes)>();
        double remaining = target;
        for (var index = 0; index < spec.Attributes.Length; index++)
        {
            var attribute = spec.Attributes[index];
            var left = spec.Attributes.Length - index;
            var count = (int)Math.Clamp(Math.Round(Math.Pow(remaining, 1.0 / left)), 1, attribute.Values.Length);
            if (left == 1)
                count = (int)Math.Clamp(Math.Ceiling(remaining), 1, attribute.Values.Length);
            remaining /= count;
            var indexes = Enumerable.Range(0, attribute.Values.Length)
                .OrderBy(_ => _random.Next())
                .Take(count)
                .Order()
                .ToArray();
            chosen.Add((attribute, indexes));
        }

        IEnumerable<IReadOnlyList<(AttributeSpec Attribute, int Index)>> combinations = [[]];
        foreach (var (attribute, indexes) in chosen)
        {
            combinations = combinations.SelectMany(prefix => indexes.Select(index =>
                (IReadOnlyList<(AttributeSpec, int)>)[.. prefix, (attribute, index)]));
        }

        var variants = new List<SeedVariant>();
        var number = 0;
        foreach (var combination in combinations)
        {
            number++;
            var attributes = combination.ToDictionary(pair => pair.Attribute.Name, pair => pair.Attribute.Values[pair.Index]);
            // 加价按「在已选值里排第几」算，而不是在全部值里的位置：只选了 256G 和 1T 时两档差一个台阶。
            var surcharge = combination.Sum(pair =>
                pair.Attribute.PriceStep * Array.IndexOf(chosen.First(item => item.Attribute == pair.Attribute).Indexes, pair.Index));
            var isDefault = number == 1;
            variants.Add(new SeedVariant(
                Guid.CreateVersion7(),
                $"SD-{spec.Code}-{sequence:D7}-{number:D2}",
                attributes.Count == 0 ? "标准款" : string.Join(" / ", attributes.Values),
                JsonSerializer.Serialize(attributes),
                _random.NextDouble() < .7 ? Barcode(++_variantSequence) : null,
                NicePrice(basePrice + surcharge),
                // 少量规格停售，但默认规格总是在售。
                isDefault || _random.NextDouble() >= .05,
                isDefault,
                Stock(basePrice)));
        }

        return variants;
    }

    private int Stock(decimal price)
    {
        var roll = _random.NextDouble();
        if (roll < .08)
            return 0;
        if (roll < .20)
            return _random.Next(1, 10);
        var ceiling = price switch { < 100 => 3000m, < 1000 => 800m, < 5000 => 200m, _ => 60m };
        return (int)LogUniform(10, ceiling);
    }

    /// <summary>EAN-13，前缀 2 是 GS1 留给店内自用的号段，不会和真实商品条码撞号。</summary>
    internal static string Barcode(long sequence)
    {
        var body = "2" + (sequence % 100_000_000_000).ToString("D11", CultureInfo.InvariantCulture);
        var sum = 0;
        for (var index = 0; index < 12; index++)
            sum += (body[index] - '0') * (index % 2 == 0 ? 1 : 3);
        return body + (char)('0' + (10 - sum % 10) % 10);
    }

    private decimal LogUniform(decimal min, decimal max)
    {
        var low = Math.Log((double)min);
        var high = Math.Log((double)max);
        return (decimal)Math.Exp(low + _random.NextDouble() * (high - low));
    }

    /// <summary>39.9、299、3499 这种常见标价。</summary>
    internal static decimal NicePrice(decimal price) => price switch
    {
        < 100 => Math.Max(9.9m, Math.Floor(price) + .9m),
        < 1000 => Math.Floor(price / 10) * 10 + 9,
        _ => Math.Floor(price / 100) * 100 + 99
    };

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
