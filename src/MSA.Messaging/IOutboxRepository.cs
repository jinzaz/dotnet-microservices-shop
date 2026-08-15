using System.Collections.Generic;

namespace MSA.Libraries.Messaging
{
    public interface IOutboxRepository
    {
        Task<IEnumerable<OutboxMessage>> GetPendingAsync(int batchSize = 100);
        Task MarkAsSentAsync(string id);
        Task AddAsync(OutboxMessage message);
    }
}
