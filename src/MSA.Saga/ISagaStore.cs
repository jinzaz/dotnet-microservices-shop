namespace MSA.Saga;
public interface ISagaStore
{
    void Add(SagaState state);
    Task<SagaState?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SagaState>> ClaimDueTimeoutsAsync(Guid lockId, int batchSize, DateTimeOffset now, TimeSpan lockDuration, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
