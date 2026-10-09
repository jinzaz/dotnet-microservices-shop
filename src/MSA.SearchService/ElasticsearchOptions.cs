namespace MSA.SearchService;

/// <summary>
/// Elasticsearch 连接与索引设置。地址、账号来自 msa.env / 环境变量 INFRA_ELASTICSEARCH_*（见 MsaEnvironment），
/// 其余在 appsettings.json 的 Elasticsearch 段。
/// 文中「生产问题 #N」的编号与 docs/search-service.md 一一对应。
/// </summary>
public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Url { get; init; } = string.Empty;

    /// <summary>优先用 API Key（可单独吊销、可限定只能读写本服务的索引），没有时才用用户名口令。</summary>
    public string? ApiKey { get; init; }

    public string? Username { get; init; }
    public string? Password { get; init; }

    /// <summary>
    /// 生产问题 #11：默认走容器内网明文、不需要它。ES 跨机器部署开了 TLS（自签 CA）时，配这张 CA 公钥证书的路径做信任锚，
    /// 而不是关掉证书校验——关掉之后任何人都能冒充 ES 拿到凭据。只在 https 地址上生效；路径不存在时服务启动失败。
    /// </summary>
    public string? CaCertificatePath { get; init; }

    /// <summary>前台只认这个别名；真实索引名为 <c>{Alias}-{时间戳}</c>，重建时原子切换（生产问题 #4）。</summary>
    public string Alias { get; init; } = "catalog-products";

    /// <summary>写入用细粒度切分召回更多词，查询用粗粒度切分减少噪音。没装 IK 时可临时改成 standard。</summary>
    public string IndexTokenizer { get; init; } = "ik_max_word";

    public string SearchTokenizer { get; init; } = "ik_smart";

    /// <summary>50 万商品的索引只有几百 MB，1 个主分片足够；分片过多反而拖慢查询与聚合。</summary>
    public int Shards { get; init; } = 1;

    public int Replicas { get; init; } = 1;

    /// <summary>
    /// 同义词，每项一组，形如「笔记本, 笔记本电脑」。存进 ES 的 synonyms set，只作用在查询分析器上，
    /// 改了重启服务即可生效，不需要重建索引（生产问题 #10）。建索引时这里为空的话，之后再加要重建一次。
    /// </summary>
    public string[] Synonyms { get; init; } = [];

    /// <summary>建索引、bulk、切别名这类管理请求的超时。</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>单次搜索在 ES 侧的超时；超时返回已收集到的部分结果，不让一个慢查询拖住页面。</summary>
    public TimeSpan SearchTimeout { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>全量重建时每页从目录导出的商品数。</summary>
    public int ExportPageSize { get; init; } = 500;
}
