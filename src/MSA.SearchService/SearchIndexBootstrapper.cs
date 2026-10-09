using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace MSA.SearchService;

/// <summary>
/// 启动引导：同义词集合要先于引用它的索引存在；主别名不存在（首次启用、被人误删）就全量建一次。
/// 生产问题 #17（ES 不可用）：ES 没起来、地址还是占位值时，服务照常启动、对外返回 503（前台回退到目录搜索），
/// 这里按指数退避一直重试，ES 一就绪就自动完成首次建索引，不需要人工介入。
/// </summary>
internal sealed class SearchIndexBootstrapper(
    ElasticsearchHttp elastic,
    IndexRebuilder rebuilder,
    IOptions<ElasticsearchOptions> options,
    TimeProvider clock,
    ILogger<SearchIndexBootstrapper> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await EnsureSynonymsAsync(stoppingToken);
                var targets = await rebuilder.AliasTargetsAsync(options.Value.Alias, stoppingToken);
                if (targets.Count > 0)
                {
                    var current = await SchemaVersionAsync(targets[0], stoppingToken);
                    if (current >= ProductSearchIndex.SchemaVersion)
                        return;
                    logger.LogInformation("Search index {Index} has schema version {Current}, service expects {Expected}; rebuilding.",
                        targets[0], current, ProductSearchIndex.SchemaVersion);
                }
                else
                {
                    logger.LogInformation("Search alias {Alias} does not exist; building it from the catalog.", options.Value.Alias);
                }

                // 多个副本同时启动时只有拿到锁的那个在建，其余的稍后再看别名是否已经就位。
                if (await rebuilder.RunAsync(stoppingToken))
                    return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (attempt == 1)
                    logger.LogWarning(exception, "Elasticsearch is not ready; search returns 503 until it is.");
                else
                    logger.LogWarning("Elasticsearch still not ready after {Attempts} attempts: {Error}", attempt, exception.Message);
            }

            var seconds = Math.Min(Math.Pow(2, Math.Min(attempt, 10)), MaxBackoff.TotalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble() * 3), clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>生产问题 #20：索引映射里记着它的结构版本；没有记录的（初版建的索引）视为 1。</summary>
    private async Task<int> SchemaVersionAsync(string index, CancellationToken cancellationToken)
    {
        var (_, body) = await elastic.SendAsync(HttpMethod.Get, $"{index}/_mapping", null, cancellationToken);
        return body?[index]?["mappings"]?["_meta"]?["schemaVersion"]?.GetValue<int>() ?? 1;
    }

    /// <summary>PUT 同义词集合是幂等的；内容变了 ES 会自动重载所有引用它的查询分析器（生产问题 #10）。</summary>
    private async Task EnsureSynonymsAsync(CancellationToken cancellationToken)
    {
        if (!ProductSearchIndex.HasSynonyms(options.Value))
            return;
        var rules = new JsonArray(options.Value.Synonyms
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Select(rule => (JsonNode)new JsonObject { ["synonyms"] = ProductSearchIndex.Normalize(rule) })
            .ToArray());
        await elastic.SendAsync(HttpMethod.Put, $"_synonyms/{ProductSearchIndex.SynonymSet(options.Value)}",
            new JsonObject { ["synonyms_set"] = rules }, cancellationToken);
    }
}
