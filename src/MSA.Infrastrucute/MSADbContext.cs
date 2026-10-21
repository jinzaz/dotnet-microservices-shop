using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MSA.Infrastrucute.Abstractions;
using MSA.Infrastrucute.Events;
using MSA.Infrastrucute.Persistence;
using MSA.Persistence.Postgres;
using System.Data;

namespace MSA.Infrastrucute;

public abstract class MSADbContext(DbContextOptions options, IMediator mediator) : DbContext(options), IUnitOfWork
{
    private IDbContextTransaction? _currentTransaction;
    public IDbContextTransaction? CurrentTransaction => _currentTransaction;
    public bool HasActiveTransaction => _currentTransaction is not null;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // 行锁写在 LINQ 里（ForUpdate / ForUpdateSkipLocked），这里统一启用把标签翻译成 SQL 的拦截器，
        // 免得某个上下文漏配后锁悄悄失效。
        optionsBuilder.UsePostgresExtensions();
        // 枚举列的 CHECK 约束按 CLR 枚举定义自动生成，新增枚举值时迁移会跟着变，不再手写取值范围。
        // 该插件依赖关系型服务，InMemory 测试库下不能启用。
        if (optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().Any())
            optionsBuilder.UseEnumCheckConstraints();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        // 所有聚合与子实体都由领域代码分配 Guid；否则经导航新增的子实体会被误判为已存在并执行 UPDATE。
        configurationBuilder.Conventions.Add(_ => new ClientGeneratedGuidKeyConvention());
    }

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        await mediator.DispatchDomainEventsAsync(this, cancellationToken);
        return await SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
            return _currentTransaction;
        _currentTransaction = await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        return _currentTransaction;
    }

    public async Task CommitTransactionAsync(IDbContextTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction != _currentTransaction)
            throw new InvalidOperationException($"Transaction {transaction.TransactionId} is not current.");

        try
        {
            await SaveEntitiesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction is not null)
                await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            if (_currentTransaction is not null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }
}
