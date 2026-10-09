using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.CatalogService.Application.Abstractions;
using MSA.CatalogService.Infrastructure;
using MSA.InventoryService.Application;
using MSA.InventoryService.Infrastructure;
using MSA.Persistence.Postgres;

namespace MSA.CommerceTests;

/// <summary>
/// pg_trgm 索引只有在 SQL 长成 LIKE / ILIKE 时才会被用上。不连数据库：拦下 EF 生成的第一条 SQL 看形状。
/// </summary>
[TestClass]
public sealed class TrigramSearchSqlTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Contains_EscapesWildcardsInUserInput()
    {
        Assert.AreEqual(@"%a\_b\%c\\d%", PostgresLike.Contains(@" a_b%c\d "));
    }

    [TestMethod]
    public async Task CatalogSearch_MatchesNameAndSkuThroughAUnionOfIndexablePredicates()
    {
        var capture = new FirstCommand();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .AddInterceptors(capture)
            .Options;
        await using var db = new CatalogDbContext(options, Mock.Of<IMediator>());
        ICatalogReadService store = new CatalogStore(db, TimeProvider.System);

        await Assert.ThrowsExactlyAsync<FirstCommand.Captured>(() =>
            store.ListProductsAsync(0, 20, publicOnly: false, "降噪_耳机", null, null, TestContext.CancellationToken));

        // 名称、SKU 各自 ILIKE 后 UNION；不能是「OR EXISTS(...)」（那样规划器会放弃索引、扫描全部商品）。
        StringAssert.Contains(capture.Sql, "UNION");
        StringAssert.Contains(capture.Sql, "ILIKE");
        StringAssert.Contains(capture.Sql, "ESCAPE");
        Assert.DoesNotContain("EXISTS", capture.Sql);
        CollectionAssert.Contains(capture.Parameters, @"%降噪\_耳机%");
    }

    [TestMethod]
    public async Task InventorySearch_UsesLikeInsteadOfStrpos()
    {
        var capture = new FirstCommand();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .AddInterceptors(capture)
            .Options;
        await using var db = new InventoryDbContext(options, Mock.Of<IMediator>());
        var handler = new ListInventoryStockHandler(db);

        await Assert.ThrowsExactlyAsync<FirstCommand.Captured>(() =>
            handler.Handle(new ListInventoryStockQuery("sd-phone", null, 0, 20), TestContext.CancellationToken));

        StringAssert.Contains(capture.Sql, "LIKE");
        Assert.DoesNotContain("strpos", capture.Sql);
        CollectionAssert.Contains(capture.Parameters, "%SD-PHONE%");
    }

    /// <summary>跳过打开连接，在第一条命令执行前记下 SQL 和参数后中断。</summary>
    private sealed class FirstCommand : IDbConnectionInterceptor, IDbCommandInterceptor
    {
        public sealed class Captured : Exception;

        public string Sql { get; private set; } = string.Empty;
        public List<object?> Parameters { get; } = [];

        public ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) =>
            throw Capture(command);

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            throw Capture(command);

        private Captured Capture(DbCommand command)
        {
            Sql = command.CommandText;
            Parameters.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
            return new Captured();
        }
    }
}
