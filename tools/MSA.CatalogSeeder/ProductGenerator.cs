using System.Globalization;
using System.Runtime.InteropServices;
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
/// <item>商品名：把每个品类词表能拼出的名字全部列出来、打乱后逐个取用，不重复也不补型号；
/// 某个品类的名字用完就不再往它分配商品，所以能生成的商品总数有上限（生成前的 <see cref="Remaining"/>）；</item>
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
    // 每个品类还没用过的名字（已打乱），从末尾取。
    private readonly List<(string Name, string? Brand)>[] _names = Shuffled(categories, new Random(seed));
    private double[]? _cumulativeWeights;
    private long _variantSequence;

    /// <summary>还能生成多少个不重名的商品；生成前就是词表的上限，导入数超过它时按它截断。</summary>
    public int Remaining => _names.Sum(pool => pool.Count);

    private static List<(string Name, string? Brand)>[] Shuffled(IReadOnlyList<SeedCategory> categories, Random random)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return categories.Select(category =>
        {
            var names = AllNames(category.Spec, seen);
            random.Shuffle(CollectionsMarshal.AsSpan(names));
            return names;
        }).ToArray();
    }

    public SeedProduct Next(long sequence)
    {
        var index = PickCategory();
        var category = categories[index];
        var spec = category.Spec;
        var pool = _names[index];
        var (name, brand) = pool[^1];
        pool.RemoveAt(pool.Count - 1);
        if (pool.Count == 0)
            _cumulativeWeights = Cumulative();
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

        var imagePool = images.GetValueOrDefault(spec.Code) ?? [];
        var imageCount = Math.Min(imagePool.Length, _random.NextDouble() switch { < .3 => 1, < .65 => 2, < .9 => 3, _ => 4 });
        var picked = imagePool.OrderBy(_ => _random.Next()).Take(imageCount).ToArray();

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

    private int PickCategory()
    {
        _cumulativeWeights ??= Cumulative();
        if (_cumulativeWeights[^1] == 0)
            throw new InvalidOperationException("词表能拼出的商品名已经用完，先按 Remaining 截断导入数。");
        var roll = _random.NextDouble() * _cumulativeWeights[^1];
        // 名字用完的品类权重为 0、累计值和前一个相同；找第一个累计值大于 roll 的，跳过它们。
        var index = Array.BinarySearch(_cumulativeWeights, roll);
        index = index < 0 ? ~index : index + 1;
        while (_names[index].Count == 0)
            index++;
        return index;
    }

    private double[] Cumulative()
    {
        var sums = new double[categories.Count];
        double total = 0;
        for (var index = 0; index < categories.Count; index++)
            sums[index] = total += _names[index].Count == 0 ? 0 : categories[index].Spec.Weight;
        return sums;
    }

    [GeneratedRegex(@"\{(?:#(?<from>\d+)-(?<to>\d+)|(?<slot>\w+))\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    /// <summary>
    /// 列出一个品类所有模板能拼出的全部名字（同时记下名字里的品牌）。以前是随机拼、撞名了就补一个随机型号，
    /// 结果 50 万商品里八成是「远山 黑胶天幕 OU-1234」这种同品牌、同分类、同名只差型号的商品，看起来全是重复的。
    /// 词表里的空串（可以省略的段）和不同模板可能拼出同一个名字，由 <paramref name="seen"/> 去重（跨品类也不重名）。
    /// </summary>
    internal static List<(string Name, string? Brand)> AllNames(CategorySpec spec, HashSet<string> seen)
    {
        var result = new List<(string Name, string? Brand)>();
        foreach (var template in spec.Templates)
        {
            // 模板拆成「字面量、候选词、字面量、候选词……」，候选词带上是不是品牌位。
            var parts = new List<(string Literal, string[] Words, bool IsBrand)>();
            var position = 0;
            foreach (Match match in Token().Matches(template))
            {
                string[] words;
                if (match.Groups["slot"].Success)
                    words = spec.Slots[match.Groups["slot"].Value];
                else
                {
                    var from = int.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
                    var to = int.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture);
                    words = Enumerable.Range(from, to - from + 1).Select(number => number.ToString(CultureInfo.InvariantCulture)).ToArray();
                }

                parts.Add((template[position..match.Index], words, match.Groups["slot"].Value == "brand"));
                position = match.Index + match.Length;
            }

            IEnumerable<(string Text, string? Brand)> names = [(string.Empty, null)];
            foreach (var (literal, words, isBrand) in parts)
            {
                names = names.SelectMany(prefix => words.Select(word =>
                    (prefix.Text + literal + word, isBrand ? word : prefix.Brand)));
            }

            var tail = template[position..];
            foreach (var (text, brand) in names)
            {
                var name = Spaces().Replace(text + tail, " ").Trim();
                if (seen.Add(name))
                    result.Add((name, brand));
            }
        }

        return result;
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
