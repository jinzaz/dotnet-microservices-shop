using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;
using MSA.Infrastrucute;
using MSA.Saga;
namespace MSA.Repositories.Persistence;

public abstract class MessagingDbContext(DbContextOptions options, IMediator mediator) : MSADbContext(options, mediator), IOutboxStore, ISagaStore
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e => { e.ToTable("outbox_messages"); e.HasKey(x => x.Id); e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.Payload).HasColumnType("jsonb"); e.Property(x => x.LastError).HasMaxLength(4000); e.HasIndex(x => new { x.Status, x.NextAttemptOnUtc, x.LockedUntilUtc }); });
        modelBuilder.Entity<SagaState>(e => { e.ToTable("saga_states"); e.HasKey(x => x.CorrelationId); e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.Data).HasColumnType("jsonb"); e.Property(x => x.SagaType).HasMaxLength(200); e.Property(x => x.State).HasMaxLength(100); e.Property(x => x.FailureReason).HasMaxLength(4000); e.HasIndex(x => new { x.Status, x.TimeoutOnUtc, x.TimeoutLockedUntilUtc }); });
        modelBuilder.Entity<InboxMessage>(e => { e.ToTable("inbox_messages"); e.HasKey(x => new { x.MessageId, x.Consumer }); e.Property(x => x.Consumer).HasMaxLength(300); });
    }
    public async Task<bool> TryBeginMessageAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        if (await InboxMessages.AnyAsync(x => x.MessageId == messageId && x.Consumer == consumer, cancellationToken)) return false;
        InboxMessages.Add(new InboxMessage(messageId, consumer, DateTimeOffset.UtcNow)); return true;
    }
    void IOutboxStore.Add(OutboxMessage message) => OutboxMessages.Add(message);
    async Task<IReadOnlyList<OutboxMessage>> IOutboxStore.ClaimPendingAsync(Guid lockId, int batchSize, int maxRetries, DateTimeOffset now, TimeSpan duration, CancellationToken ct)
    { var rows = await OutboxMessages.Where(x => x.RetryCount < maxRetries && x.NextAttemptOnUtc <= now && (x.Status == OutboxStatus.Pending || x.Status == OutboxStatus.Processing && x.LockedUntilUtc < now)).OrderBy(x => x.OccurredOnUtc).Take(batchSize).ToListAsync(ct); foreach (var row in rows) row.Claim(lockId, now.Add(duration)); await base.SaveChangesAsync(ct); return rows; }
    Task IOutboxStore.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
    void ISagaStore.Add(SagaState state) => SagaStates.Add(state);
    Task<SagaState?> ISagaStore.FindAsync(Guid id, CancellationToken ct) => SagaStates.SingleOrDefaultAsync(x => x.CorrelationId == id, ct);
    async Task<IReadOnlyList<SagaState>> ISagaStore.ClaimDueTimeoutsAsync(Guid lockId, int size, DateTimeOffset now, TimeSpan duration, CancellationToken ct)
    { var rows = await SagaStates.Where(x => x.Status == SagaStatus.Running && x.TimeoutOnUtc <= now && (x.TimeoutLockId == null || x.TimeoutLockedUntilUtc < now)).OrderBy(x => x.TimeoutOnUtc).Take(size).ToListAsync(ct); foreach (var row in rows) row.ClaimTimeout(lockId, now.Add(duration), now); await base.SaveChangesAsync(ct); return rows; }
    Task ISagaStore.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
}
