using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public class EfSagaRepository : ISagaRepository
    {
        private readonly OutboxDbContext _db;

        public EfSagaRepository(OutboxDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(SagaState state)
        {
            await _db.SagaStates.AddAsync(state);
            await _db.SaveChangesAsync();
        }

        public async Task<SagaState> GetByCorrelationIdAsync(string sagaType, string correlationId)
        {
            return await _db.SagaStates.FirstOrDefaultAsync(s => s.SagaType == sagaType && s.CorrelationId == correlationId);
        }

        public async Task<IEnumerable<SagaState>> GetActiveAsync(string sagaType)
        {
            return await _db.SagaStates.Where(s => s.SagaType == sagaType && !s.Completed).ToListAsync();
        }

        public async Task UpdateAsync(SagaState state)
        {
            _db.SagaStates.Update(state);
            await _db.SaveChangesAsync();
        }
    }
}
