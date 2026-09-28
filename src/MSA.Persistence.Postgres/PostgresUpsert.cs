using System.Data.Common;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace MSA.Persistence.Postgres;

/// <summary>
/// INSERT ... ON CONFLICT 的实体化封装：语句由 EF 模型（表名、列名、值转换、类型映射）生成，
/// 调用方只写实体和冲突列，不需要拼 SQL，也不经过变更跟踪。
/// </summary>
public static class PostgresUpsertExtensions
{
    /// <summary>
    /// 幂等插入：冲突时什么也不做。返回 true 表示这一次真的插入了，
    /// 正是 Inbox 去重这类「原子抢占」需要的语义（先查后插在并发下并不安全）。
    /// </summary>
    public static async Task<bool> InsertOnConflictDoNothingAsync<TEntity>(
        this DbSet<TEntity> set,
        TEntity entity,
        Expression<Func<TEntity, object?>>? conflictTarget = null,
        CancellationToken cancellationToken = default)
        where TEntity : class =>
        await set.ExecuteInsertAsync(entity, conflictTarget, updatedProperties: null, doUpdate: false, cancellationToken) == 1;

    /// <summary>
    /// 插入或更新。<paramref name="updatedProperties"/> 省略时更新除冲突列以外的所有列，
    /// 更新值取自本次要插入的行（EXCLUDED）。
    /// </summary>
    public static Task<int> UpsertAsync<TEntity>(
        this DbSet<TEntity> set,
        TEntity entity,
        Expression<Func<TEntity, object?>>? conflictTarget = null,
        Expression<Func<TEntity, object?>>? updatedProperties = null,
        CancellationToken cancellationToken = default)
        where TEntity : class =>
        set.ExecuteInsertAsync(entity, conflictTarget, updatedProperties, doUpdate: true, cancellationToken);

    private static async Task<int> ExecuteInsertAsync<TEntity>(
        this DbSet<TEntity> set,
        TEntity entity,
        Expression<Func<TEntity, object?>>? conflictTarget,
        Expression<Func<TEntity, object?>>? updatedProperties,
        bool doUpdate,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(entity);

        var context = set.GetService<ICurrentDbContext>().Context;
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of the model.");
        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped to a table.");
        var helper = context.GetService<ISqlGenerationHelper>();

        var columns = entityType.GetProperties()
            .Where(property => !property.IsShadowProperty() && property.GetColumnName(table) is not null)
            .ToArray();
        if (columns.Length == 0)
            throw new InvalidOperationException($"{typeof(TEntity).Name} has no writable columns.");

        var conflictColumns = conflictTarget is null
            ? entityType.FindPrimaryKey()?.Properties.ToArray()
                ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key; pass a conflict target.")
            : Resolve(entityType, conflictTarget);
        var updateColumns = updatedProperties is null
            ? columns.Where(column => !conflictColumns.Contains(column)).ToArray()
            : Resolve(entityType, updatedProperties);

        var sql = BuildSql(helper, table, columns, conflictColumns, doUpdate ? updateColumns : []);
        return await PostgresCommand.ExecuteNonQueryAsync(
            context,
            sql,
            command => AddParameters(command, helper, columns, entity, table),
            cancellationToken);
    }

    private static string BuildSql(
        ISqlGenerationHelper helper,
        StoreObjectIdentifier table,
        IReadOnlyList<IProperty> columns,
        IReadOnlyList<IProperty> conflictColumns,
        IReadOnlyList<IProperty> updateColumns)
    {
        var builder = new StringBuilder("INSERT INTO ")
            .Append(helper.DelimitIdentifier(table.Name, table.Schema))
            .Append(" (")
            .AppendJoin(", ", columns.Select(column => helper.DelimitIdentifier(column.GetColumnName(table)!)))
            .Append(") VALUES (")
            .AppendJoin(", ", columns.Select((_, index) => helper.GenerateParameterName($"p{index}")))
            .Append(") ON CONFLICT (")
            .AppendJoin(", ", conflictColumns.Select(column => helper.DelimitIdentifier(column.GetColumnName(table)!)))
            .Append(')');

        if (updateColumns.Count == 0)
            return builder.Append(" DO NOTHING").ToString();

        return builder
            .Append(" DO UPDATE SET ")
            .AppendJoin(", ", updateColumns.Select(column =>
            {
                var name = helper.DelimitIdentifier(column.GetColumnName(table)!);
                return $"{name} = EXCLUDED.{name}";
            }))
            .ToString();
    }

    private static void AddParameters<TEntity>(
        DbCommand command,
        ISqlGenerationHelper helper,
        IReadOnlyList<IProperty> columns,
        TEntity entity,
        StoreObjectIdentifier table)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var property = columns[index];
            // 传 CLR 原值即可：类型映射内部会套用模型里配置的值转换（枚举、jsonb、DateTimeOffset 等），
            // 这里再转一次会导致二次转换。
            var mapping = property.FindRelationalTypeMapping(table)
                ?? throw new InvalidOperationException($"Property {property.Name} has no relational type mapping.");
            // 参数名要带上提供程序的前缀（@ / :），否则 SQLite 这类严格匹配的驱动会说找不到参数；
            // CreateParameter 只负责创建，挂到命令上要自己来。
            command.Parameters.Add(mapping.CreateParameter(
                command,
                helper.GenerateParameterName($"p{index}"),
                ReadValue(property, entity!),
                property.IsNullable));
        }
    }

    /// <summary>私有 setter 的属性用属性读取，备份字段兜底，全程不碰变更跟踪器。</summary>
    private static object? ReadValue(IProperty property, object entity) =>
        property.PropertyInfo?.GetValue(entity)
        ?? property.FieldInfo?.GetValue(entity);

    private static IProperty[] Resolve<TEntity>(IEntityType entityType, Expression<Func<TEntity, object?>> selector)
    {
        var names = PropertyNames(selector.Body);
        if (names.Count == 0)
            throw new ArgumentException("Select at least one property, e.g. entity => new { entity.A, entity.B }.", nameof(selector));
        return names
            .Select(name => entityType.FindProperty(name)
                ?? throw new ArgumentException($"{entityType.DisplayName()} has no property '{name}'.", nameof(selector)))
            .ToArray();
    }

    private static List<string> PropertyNames(Expression body) => body switch
    {
        UnaryExpression unary => PropertyNames(unary.Operand),
        MemberExpression member => [member.Member.Name],
        NewExpression anonymous => anonymous.Arguments.SelectMany(PropertyNames).ToList(),
        _ => []
    };
}
