using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Application.Abstractions;

namespace MSA.BasketService.Infrastructure;

public sealed class PostgresBasketMutationLock(BasketDbContext db) : IBasketMutationLock
{
    public async Task AcquireAsync(string userId, CancellationToken cancellationToken)
    {
        if (!db.HasActiveTransaction)
            throw new InvalidOperationException("Basket mutation lock requires an active transaction.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({userId.Trim()}, 0))",
            cancellationToken);
    }
}
