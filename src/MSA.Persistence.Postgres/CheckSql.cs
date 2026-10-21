namespace MSA.Persistence.Postgres;

/// <summary>
/// 手写 CHECK 约束的 SQL 片段。列名一律传 <c>nameof(...)</c>，属性改名时编译期就能发现；
/// 枚举取值范围不在这里，由 EFCore.CheckConstraints 按枚举定义自动生成。
/// </summary>
public static class CheckSql
{
    public static string Col(string column) => $"\"{column}\"";

    /// <summary><c>"earlier" &lt; "later"</c>，用于时间窗口。</summary>
    public static string Before(string earlier, string later) => $"{Col(earlier)} < {Col(later)}";

    /// <summary><c>"later" &gt;= "earlier"</c>，用于审计时间戳。</summary>
    public static string NotBefore(string later, string earlier) => $"{Col(later)} >= {Col(earlier)}";

    /// <summary><c>"column" = upper(trim("source"))</c>；省略 source 时约束列自身已标准化。</summary>
    public static string UpperTrimmed(string column, string? source = null) =>
        $"{Col(column)} = upper(trim({Col(source ?? column)}))";

    public static string CurrencyCode(string column) => $"{Col(column)} ~ '^[A-Z]{{3}}$'";

    public static string Sha256Hex(string column) => $"{Col(column)} ~ '^[0-9A-F]{{64}}$'";

    public static string JsonObject(string column) => $"jsonb_typeof({Col(column)}) = 'object'";
}
