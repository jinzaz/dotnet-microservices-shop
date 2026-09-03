using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.EventBusOutbox;
using MSA.Infrastrucute;
using MSA.OrderService.Domain;
using MSA.Saga;
namespace MSA.OrderService.Infrastructure;
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, IMediator mediator) : MSADbContext(options, mediator), IOutboxStore, ISagaStore
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity => { entity.ToTable("orders"); entity.HasKey(x => x.Id); entity.Property(x => x.ProductId).HasMaxLength(100); entity.Property(x => x.Amount).HasPrecision(18, 2); entity.Property(x => x.Currency).HasMaxLength(3); });
        modelBuilder.Entity<OutboxMessage>(entity => { entity.ToTable("outbox_messages"); entity.HasKey(x => x.Id); entity.Property(x => x.Version).IsConcurrencyToken(); entity.Property(x => x.Payload).HasColumnType("jsonb"); entity.Property(x => x.LastError).HasMaxLength(4000); entity.HasIndex(x => new { x.Status, x.NextAttemptOnUtc, x.LockedUntilUtc }); });
        modelBuilder.Entity<SagaState>(entity => { entity.ToTable("saga_states"); entity.HasKey(x => x.CorrelationId); entity.Property(x => x.Version).IsConcurrencyToken(); entity.Property(x => x.Data).HasColumnType("jsonb"); entity.Property(x => x.SagaType).HasMaxLength(200); entity.Property(x => x.State).HasMaxLength(100); entity.Property(x => x.FailureReason).HasMaxLength(4000); entity.HasIndex(x => new { x.Status, x.TimeoutOnUtc, x.TimeoutLockedUntilUtc }); });
    }
    void IOutboxStore.Add(OutboxMessage message) => OutboxMessages.Add(message);
    async Task<IReadOnlyList<OutboxMessage>> IOutboxStore.ClaimPendingAsync(Guid lockId, int batchSize, int maxRetries, DateTimeOffset now, TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        var messages = await OutboxMessages.Where(x => x.RetryCount < maxRetries && x.NextAttemptOnUtc <= now && (x.Status == OutboxStatus.Pending || x.Status == OutboxStatus.Processing && x.LockedUntilUtc < now)).OrderBy(x => x.OccurredOnUtc).Take(batchSize).ToListAsync(cancellationToken);
        foreach (var message in messages) message.Claim(lockId, now.Add(lockDuration));
        await base.SaveChangesAsync(cancellationToken); return messages;
    }
    Task IOutboxStore.SaveChangesAsync(CancellationToken cancellationToken) => base.SaveChangesAsync(cancellationToken);
    void ISagaStore.Add(SagaState state) => SagaStates.Add(state);
    Task<SagaState?> ISagaStore.FindAsync(Guid correlationId, CancellationToken cancellationToken) => SagaStates.SingleOrDefaultAsync(x => x.CorrelationId == correlationId, cancellationToken);
    async Task<IReadOnlyList<SagaState>> ISagaStore.ClaimDueTimeoutsAsync(Guid lockId, int batchSize, DateTimeOffset now, TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        var sagas = await SagaStates.Where(x => x.Status == SagaStatus.Running && x.TimeoutOnUtc <= now && (x.TimeoutLockId == null || x.TimeoutLockedUntilUtc < now)).OrderBy(x => x.TimeoutOnUtc).Take(batchSize).ToListAsync(cancellationToken);
        foreach (var saga in sagas) saga.ClaimTimeout(lockId, now.Add(lockDuration), now);
        await base.SaveChangesAsync(cancellationToken); return sagas;
    }
    Task ISagaStore.SaveChangesAsync(CancellationToken cancellationToken) => base.SaveChangesAsync(cancellationToken);
}
