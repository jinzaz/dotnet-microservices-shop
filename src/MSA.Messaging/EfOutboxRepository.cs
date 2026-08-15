using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public class EfOutboxRepository : IOutboxRepository
    {
        private readonly OutboxDbContext _db;

        public EfOutboxRepository(OutboxDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(OutboxMessage message)
        {
            await _db.OutboxMessages.AddAsync(message);
            await _db.SaveChangesAsync();
        }

        public async Task<IEnumerable<OutboxMessage>> GetPendingAsync(int batchSize = 100)
        {
            return await _db.OutboxMessages.Where(x => !x.Sent).Take(batchSize).ToListAsync();
        }

        public async Task MarkAsSentAsync(string id)
        {
            var msg = await _db.OutboxMessages.FindAsync(id);
            if (msg != null)
            {
                msg.Sent = true;
                await _db.SaveChangesAsync();
            }
        }
    }
}
