using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;
using MSA.IdentityService.Domain;
using MSA.Persistence.Postgres;
using MSA.Repositories.EntityConfigurations;
using MSA.Repositories.Persistence;

namespace MSA.IdentityService;

/// <summary>
/// 必须继承 IdentityDbContext，没法继承 MessagingDbContext，所以只接 Outbox（Identity 只发不收）：
/// 表映射和抢占逻辑复用 MSA.Repositories 的同一份实现，注册与事件在同一事务里提交。
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IOutboxStore
{
    internal DbSet<SmsVerificationMessage> SmsMessages => Set<SmsVerificationMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // Outbox 抢占用 ForUpdateSkipLocked，需要把行锁标签翻译成 SQL 的拦截器。
        optionsBuilder.UsePostgresExtensions();
        optionsBuilder.UseEnumCheckConstraints();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        builder.ApplyConfiguration(new OutboxMessageConfiguration("identity"));
    }

    void IOutboxStore.Add(OutboxMessage message) => OutboxMessages.Add(message);

    Task<IReadOnlyList<OutboxMessage>> IOutboxStore.ClaimPendingAsync(
        Guid lockId,
        int batchSize,
        int maxRetries,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        this.ClaimPendingOutboxAsync(lockId, batchSize, maxRetries, now, duration, cancellationToken);

    Task IOutboxStore.SaveChangesAsync(CancellationToken cancellationToken) => base.SaveChangesAsync(cancellationToken);
}
