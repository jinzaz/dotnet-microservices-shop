using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
using MSA.UserService.Domain;

namespace MSA.UserService.Infrastructure;

public sealed class PostgresUserBusinessLock(UserDbContext db) : IUserBusinessLock
{
    public Task AcquireAsync(string userId, string purpose, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return db.AcquireTransactionLockAsync($"user:{purpose.Trim()}:{userId.Trim()}", cancellationToken);
    }
}
