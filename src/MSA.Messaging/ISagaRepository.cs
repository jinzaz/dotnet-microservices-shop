using System.Collections.Generic;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public interface ISagaRepository
    {
        Task<SagaState> GetByCorrelationIdAsync(string sagaType, string correlationId);
        Task AddAsync(SagaState state);
        Task UpdateAsync(SagaState state);
        Task<IEnumerable<SagaState>> GetActiveAsync(string sagaType);
    }
}
