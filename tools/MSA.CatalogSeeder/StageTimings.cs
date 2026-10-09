using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MSA.CatalogSeeder;

/// <summary>
/// 各个库的累计写入耗时。并行写入时各阶段同时进行，累计值加起来会超过实际用时；
/// 哪一项最大，瓶颈就在哪个库（例如目录库的索引多、外键多，通常最慢）。
/// </summary>
internal sealed class StageTimings
{
    private readonly ConcurrentDictionary<string, long> _ticks = new(StringComparer.Ordinal);

    public async Task Measure(string stage, Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await action();
        }
        finally
        {
            _ticks.AddOrUpdate(stage, Stopwatch.GetElapsedTime(started).Ticks, (_, current) => current + Stopwatch.GetElapsedTime(started).Ticks);
        }
    }

    public string Describe() =>
        "各库累计写入耗时（并行时总和大于实际用时，最大的那项就是瓶颈）：" + string.Join("，", _ticks
            .OrderByDescending(pair => pair.Value)
            .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {TimeSpan.FromTicks(pair.Value).TotalSeconds:N0} 秒")));
}
