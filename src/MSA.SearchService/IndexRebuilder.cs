using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MSA.Grpc.Contracts.Catalog;

namespace MSA.SearchService;

public sealed record SearchIndexStatusDto(
    string Alias,
    IReadOnlyList<string> Indices,
    long? DocumentCount,
    bool Rebuilding,
    DateTimeOffset? RebuildStartedOnUtc,
    DateTimeOffset? LastRebuildCompletedOnUtc,
    long? LastRebuildDocumentCount,
    string? LastError);

/// <summary>
/// 全量重建（首次启用、改映射 / 分词 / 同义词结构、索引损坏）。
/// <para>
/// 生产问题 #4（零停机重建、重建期间不丢变更）：
/// <list type="number">
/// <item>建带时间戳的新索引，同时挂上 <c>{alias}-building</c> 别名；</item>
/// <item>等所有副本的 <see cref="BuildingAliasCache"/> 都看到它——从此增量写入同时写新旧两个索引；</item>
/// <item>按主键分页从目录导出全部可搜商品写进新索引（外部版本号保证导出的旧快照覆盖不了增量写入的新快照）；</item>
/// <item>恢复副本与刷新，一个 _aliases 请求原子地把主别名切到新索引、摘掉 building 别名；</item>
/// <item>删掉旧索引。</item>
/// </list>
/// 切换前前台一直查旧索引，没有「空结果」或「结果减半」的窗口。
/// </para>
/// <para>
/// 跨副本互斥：在 ES 里建一个固定名字的锁索引——建索引是原子的，第二个人会拿到 resource_already_exists。
/// 搜索服务没有自己的数据库，协调状态全部放在 ES 里。进程崩溃留下的锁超过 <see cref="StaleLockAfter"/> 视为失效。
/// </para>
/// </summary>
internal sealed class IndexRebuilder(
    ElasticsearchHttp elastic,
    IServiceScopeFactory scopes,
    BuildingAliasCache building,
    IOptions<ElasticsearchOptions> options,
    TimeProvider clock,
    ILogger<IndexRebuilder> logger)
{
    private static readonly TimeSpan StaleLockAfter = TimeSpan.FromHours(2);

    private int _running;
    private DateTimeOffset? _startedOnUtc;
    private DateTimeOffset? _completedOnUtc;
    private long? _completedCount;
    private volatile string? _lastError;

    private ElasticsearchOptions Options => options.Value;
    private string LockIndex => $"{Options.Alias}_rebuild_lock";

    /// <summary>
    /// 拿到锁后在后台重建并立即返回 true；别的副本或本进程已在重建时返回 false。
    /// <paramref name="force"/> 用于清掉崩溃遗留、但还没过期的锁。
    /// </summary>
    public async Task<bool> TryStartAsync(bool force, CancellationToken stopping)
    {
        if (!await TryEnterAsync(force, stopping))
            return false;
        _ = Task.Run(() => RunLockedAsync(stopping), CancellationToken.None);
        return true;
    }

    /// <summary>启动引导用：同步跑完整次重建。返回 false 表示别人正在重建。</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (!await TryEnterAsync(force: false, cancellationToken))
            return false;
        await RunLockedAsync(cancellationToken, rethrow: true);
        return true;
    }

    private async Task<bool> TryEnterAsync(bool force, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return false;
        try
        {
            if (await TryAcquireLockAsync(force, cancellationToken))
                return true;
        }
        catch
        {
            Volatile.Write(ref _running, 0);
            throw;
        }

        Volatile.Write(ref _running, 0);
        return false;
    }

    private async Task RunLockedAsync(CancellationToken cancellationToken, bool rethrow = false)
    {
        _startedOnUtc = clock.GetUtcNow();
        try
        {
            _completedCount = await RebuildAsync(cancellationToken);
            _completedOnUtc = clock.GetUtcNow();
            _lastError = null;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            logger.LogError(exception, "Search index rebuild failed; the previous index stays live.");
            if (rethrow)
                throw;
        }
        finally
        {
            await ReleaseLockAsync();
            _startedOnUtc = null;
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<bool> TryAcquireLockAsync(bool force, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (status, _) = await elastic.SendAsync(HttpMethod.Put, LockIndex, new JsonObject
            {
                ["settings"] = new JsonObject { ["number_of_shards"] = 1, ["number_of_replicas"] = 0 },
                ["mappings"] = new JsonObject
                {
                    ["_meta"] = new JsonObject
                    {
                        ["owner"] = Environment.MachineName,
                        ["startedOnUtc"] = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)
                    }
                }
            }, cancellationToken, HttpStatusCode.BadRequest);
            if (status == HttpStatusCode.OK)
                return true;

            var startedOn = await LockStartedOnAsync(cancellationToken);
            if (!force && startedOn is { } started && clock.GetUtcNow() - started < StaleLockAfter)
                return false;
            logger.LogWarning("Removing a {Kind} search rebuild lock taken at {StartedOn}.", force ? "forced" : "stale", startedOn);
            await elastic.SendAsync(HttpMethod.Delete, LockIndex, null, cancellationToken, HttpStatusCode.NotFound);
        }

        return false;
    }

    private async Task<DateTimeOffset?> LockStartedOnAsync(CancellationToken cancellationToken)
    {
        var (_, body) = await elastic.SendAsync(HttpMethod.Get, $"{LockIndex}/_mapping", null, cancellationToken, HttpStatusCode.NotFound);
        var value = body?[LockIndex]?["mappings"]?["_meta"]?["startedOnUtc"]?.ToString();
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startedOn) ? startedOn : null;
    }

    private async Task ReleaseLockAsync()
    {
        try
        {
            await elastic.SendAsync(HttpMethod.Delete, LockIndex, null, CancellationToken.None, HttpStatusCode.NotFound);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not release the search rebuild lock; it expires after {StaleAfter}.", StaleLockAfter);
        }
    }

    private async Task<long> RebuildAsync(CancellationToken cancellationToken)
    {
        var index = $"{Options.Alias}-{clock.GetUtcNow():yyyyMMddHHmmssfff}";
        var buildingAlias = ProductIndexWriter.BuildingAlias(Options);
        var started = clock.GetUtcNow();
        try
        {
            var definition = ProductSearchIndex.Definition(Options);
            definition["aliases"] = new JsonObject { [buildingAlias] = new JsonObject() };
            await elastic.SendAsync(HttpMethod.Put, index, definition, cancellationToken);
            building.Forget();
            // 等所有副本的别名缓存都过期一轮，之后发生的变更一定会同时写进新索引。
            await Task.Delay(BuildingAliasCache.Duration * 2, clock, cancellationToken);

            var indexed = await ExportAsync(index, cancellationToken);

            // 生产问题 #13：灌数据时副本为 0、不刷新（建索引时已设置），灌完再恢复，导入速度快数倍。
            await elastic.SendAsync(HttpMethod.Put, $"{index}/_settings", new JsonObject
            {
                ["index"] = new JsonObject { ["number_of_replicas"] = Options.Replicas, ["refresh_interval"] = "1s" }
            }, cancellationToken);
            await elastic.SendAsync(HttpMethod.Post, $"{index}/_refresh", null, cancellationToken);
            // 单节点集群副本永远分配不出去，所以等 yellow（主分片就绪）而不是 green。
            await elastic.SendAsync(HttpMethod.Get, $"_cluster/health/{index}?wait_for_status=yellow&timeout=60s", null, cancellationToken);

            var actions = new JsonArray();
            foreach (var old in await AliasTargetsAsync(Options.Alias, cancellationToken))
                actions.Add(new JsonObject { ["remove"] = new JsonObject { ["index"] = old, ["alias"] = Options.Alias } });
            actions.Add(new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = Options.Alias } });
            actions.Add(new JsonObject { ["remove"] = new JsonObject { ["index"] = index, ["alias"] = buildingAlias } });
            await elastic.SendAsync(HttpMethod.Post, "_aliases", new JsonObject { ["actions"] = actions }, cancellationToken);
            building.Forget();

            await DeleteStaleIndicesAsync(index, cancellationToken);
            logger.LogInformation("Search index {Index} rebuilt with {Count} products in {Elapsed}; alias {Alias} switched.",
                index, indexed, clock.GetUtcNow() - started, Options.Alias);
            return indexed;
        }
        catch
        {
            // 删掉半成品（building 别名随之消失）；主别名仍指向旧索引，前台不受影响。
            try
            {
                await elastic.SendAsync(HttpMethod.Delete, index, null, CancellationToken.None, HttpStatusCode.NotFound);
            }
            catch (Exception cleanup)
            {
                logger.LogWarning(cleanup, "Could not delete the partial search index {Index}.", index);
            }

            building.Forget();
            throw;
        }
    }

    private async Task<long> ExportAsync(string index, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchFeed.CatalogSearchFeedClient>();
        var writer = scope.ServiceProvider.GetRequiredService<ProductIndexWriter>();
        long indexed = 0;
        var after = string.Empty;
        do
        {
            var page = await catalog.ExportProductsAsync(
                new ExportSearchProductsRequest { AfterId = after, PageSize = Options.ExportPageSize },
                cancellationToken: cancellationToken);
            if (page.Products.Count > 0)
            {
                var bulk = ProductIndexWriter.BuildBulk(page.Products.Select(product => (Guid.Parse(product.Id), (SearchProduct?)product)));
                writer.EnsureSucceeded(await elastic.BulkAsync($"{index}/_bulk", bulk, cancellationToken), missingIndexIsFine: false);
            }

            var before = indexed;
            indexed += page.Products.Count;
            if (indexed / 50_000 != before / 50_000)
                logger.LogInformation("Search rebuild progress: {Count} products written to {Index}.", indexed, index);
            after = page.NextAfterId;
        } while (after.Length > 0);

        return indexed;
    }

    internal async Task<IReadOnlyList<string>> AliasTargetsAsync(string alias, CancellationToken cancellationToken)
    {
        var (status, body) = await elastic.SendAsync(HttpMethod.Get, $"_alias/{alias}", null, cancellationToken, HttpStatusCode.NotFound);
        return status == HttpStatusCode.NotFound || body is not JsonObject indices
            ? []
            : indices.Select(index => index.Key).ToArray();
    }

    /// <summary>旧索引，以及以前重建中途崩溃留下的半成品，统统清掉，免得悄悄占满磁盘。</summary>
    private async Task DeleteStaleIndicesAsync(string current, CancellationToken cancellationToken)
    {
        var (_, body) = await elastic.SendAsync(HttpMethod.Get, $"_cat/indices/{Options.Alias}-*?format=json&h=index", null, cancellationToken);
        foreach (var name in body?.AsArray().Select(row => row?["index"]?.ToString()).OfType<string>() ?? [])
        {
            if (name != current)
                await elastic.SendAsync(HttpMethod.Delete, name, null, cancellationToken, HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// 生产问题 #16（监控）：文档数和目录可搜商品数对不上、重建一直失败，都从这里看；
    /// 同步积压看 RabbitMQ 里 msa.search 队列的长度与死信队列 msa.search.dlq。
    /// </summary>
    public async Task<SearchIndexStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> indices = [];
        long? count = null;
        var error = _lastError;
        try
        {
            indices = await AliasTargetsAsync(Options.Alias, cancellationToken);
            if (indices.Count > 0)
            {
                var (_, body) = await elastic.SendAsync(HttpMethod.Get, $"{Options.Alias}/_count", null, cancellationToken);
                count = body?["count"]?.GetValue<long>();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or ElasticsearchException or TaskCanceledException)
        {
            error = exception.Message;
        }

        return new SearchIndexStatusDto(
            Options.Alias,
            indices,
            count,
            Volatile.Read(ref _running) == 1,
            _startedOnUtc,
            _completedOnUtc,
            _completedCount,
            error);
    }
}
