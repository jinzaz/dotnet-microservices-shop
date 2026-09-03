namespace MSA.EventBusOutbox;
public interface IOutboxStore
{
    void Add(OutboxMessage message);
    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(Guid lockId, int batchSize, int maxRetries, DateTimeOffset now, TimeSpan lockDuration, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
