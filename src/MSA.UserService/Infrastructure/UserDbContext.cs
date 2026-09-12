using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute;
using MSA.UserService.Domain;

namespace MSA.UserService.Infrastructure;

public sealed class UserDbContext(
    DbContextOptions<UserDbContext> options,
    IMediator mediator) : MSADbContext(options, mediator)
{
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<LoyaltyPointAccount> LoyaltyPointAccounts => Set<LoyaltyPointAccount>();
    public DbSet<LoyaltyPointEntry> LoyaltyPointEntries => Set<LoyaltyPointEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsurePointLedgerIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsurePointLedgerIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserDbContext).Assembly);
    }

    private void EnsurePointLedgerIsAppendOnly()
    {
        // 积分余额可以修正，但历史流水只允许追加，确保审计链和余额推导不被改写。
        if (ChangeTracker.Entries<LoyaltyPointEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Loyalty point ledger entries are immutable.");
        }
    }
}
