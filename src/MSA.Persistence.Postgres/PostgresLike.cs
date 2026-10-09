namespace MSA.Persistence.Postgres;

/// <summary>
/// 把用户输入的关键词变成「包含」匹配的 LIKE / ILIKE 模式。配合 pg_trgm 的 GIN 三元组索引
/// （<c>gin_trgm_ops</c>），<c>'%关键词%'</c> 这种前后都带通配符的查询也能走索引，而不是全表扫描。
/// <para>
/// 用户输入里的 <c>%</c>、<c>_</c> 和转义符本身要先转义：否则搜「_」会匹配任意字符、搜「%」会匹配全部，
/// 既查得不对，也会让本来能走索引的查询退化成全表扫描。调用时转义符传 <see cref="Escape"/>：
/// <c>EF.Functions.ILike(column, PostgresLike.Contains(term), PostgresLike.Escape)</c>。
/// </para>
/// <para>
/// 三元组索引要求关键词至少 3 个字符（中文也按字符算）：1–2 个字符的关键词拆不出三元组，仍然要扫全表。
/// 这类短词搜索交给搜索服务（Elasticsearch），数据库这里只做后台检索和降级兜底。
/// </para>
/// </summary>
public static class PostgresLike
{
    public const string Escape = "\\";

    public static string Contains(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var escaped = term.Trim()
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
