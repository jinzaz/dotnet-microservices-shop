using Microsoft.EntityFrameworkCore;
using MSA.UserService.Domain;

namespace MSA.UserService.Infrastructure;

public sealed class PostgresUserBusinessLock(UserDbContext db) : IUserBusinessLock
{
    public Task AcquireAsync(string userId, string purpose, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var lockKey = $"user:{purpose.Trim()}:{userId.Trim()}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}
