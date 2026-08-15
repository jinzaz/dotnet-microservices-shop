using Microsoft.EntityFrameworkCore;

namespace MSA.Libraries.Messaging
{
    public class OutboxDbContext : DbContext
    {
        public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<SagaState> SagaStates { get; set; }
    }
}
