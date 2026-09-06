using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Domain;

namespace MSA.IdentityService.Infrastructure;

public sealed class PostgresUserBusinessLock(ApplicationDbContext db) : IUserBusinessLock
{
    public Task AcquireAsync(string userId, string purpose, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var lockKey = $"identity:{purpose.Trim()}:{userId.Trim()}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
