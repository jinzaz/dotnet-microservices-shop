using Microsoft.EntityFrameworkCore;

namespace MSA.Persistence.Postgres;

public static class PostgresDbContextOptionsExtensions
{
    /// <summary>
    /// 启用本库的 PostgreSQL 扩展（目前是把 LINQ 上的行锁标签翻译成 FOR UPDATE / FOR SHARE 子句）。
    /// 可以重复调用：拦截器是单例，EF 会按实例去重。
    /// </summary>
    public static DbContextOptionsBuilder UsePostgresExtensions(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        return optionsBuilder.AddInterceptors(PostgresRowLockingInterceptor.Instance);
    }
}
