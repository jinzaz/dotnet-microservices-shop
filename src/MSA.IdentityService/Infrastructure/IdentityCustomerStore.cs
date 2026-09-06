using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Application;
using MSA.IdentityService.Domain;

namespace MSA.IdentityService.Infrastructure;

public sealed class IdentityCustomerStore(ApplicationDbContext db) :
    IUserAddressRepository,
    ILoyaltyPointRepository,
    IIdentityCustomerReadStore
{
    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken);

    public Task<int> CountActiveAsync(string userId, CancellationToken cancellationToken) =>
        db.UserAddresses.CountAsync(
            address => address.UserId == userId
                && address.IsEnabled
                && address.DeletedOnUtc == null,
            cancellationToken);

    public Task<UserAddress?> GetAsync(
        string userId,
        Guid addressId,
        CancellationToken cancellationToken) =>
        db.UserAddresses.SingleOrDefaultAsync(
            address => address.Id == addressId
                && address.UserId == userId
                && address.IsEnabled
                && address.DeletedOnUtc == null,
            cancellationToken);

    public Task ClearDefaultAsync(
        string userId,
        DateTimeOffset changedOnUtc,
        CancellationToken cancellationToken) =>
        db.UserAddresses
            .Where(address => address.UserId == userId && address.IsDefault)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(address => address.IsDefault, false)
                    .SetProperty(address => address.UpdatedOnUtc, changedOnUtc)
                    .SetProperty(address => address.Version, address => address.Version + 1),
                cancellationToken);

    public void Add(UserAddress address) => db.UserAddresses.Add(address);

    public Task<LoyaltyPointAccount?> GetAccountAsync(
        string userId,
        CancellationToken cancellationToken) =>
        db.LoyaltyPointAccounts.SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);

    public Task<LoyaltyPointEntry?> GetEntryAsync(
        string userId,
        string businessKey,
        CancellationToken cancellationToken) =>
        db.LoyaltyPointEntries.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.UserId == userId && entry.BusinessKey == businessKey,
            cancellationToken);

    public void AddAccount(LoyaltyPointAccount account) => db.LoyaltyPointAccounts.Add(account);

    public void AddEntry(LoyaltyPointEntry entry) => db.LoyaltyPointEntries.Add(entry);

    public async Task<IReadOnlyList<UserAddressResponse>> ListAddressesAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await db.UserAddresses
            .AsNoTracking()
            .Where(address => address.UserId == userId
                && address.IsEnabled
                && address.DeletedOnUtc == null)
            .OrderByDescending(address => address.IsDefault)
            .ThenByDescending(address => address.UpdatedOnUtc)
            .Select(address => new UserAddressResponse(
                address.Id,
                address.Label,
                address.RecipientName,
                address.PhoneNumber,
                address.CountryCode,
                address.Province,
                address.City,
                address.District,
                address.Street,
                address.PostalCode,
                address.IsDefault,
                address.CreatedOnUtc,
                address.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

    public Task<UserAddressResponse?> GetAddressAsync(
        string userId,
        Guid addressId,
        CancellationToken cancellationToken) =>
        db.UserAddresses
            .AsNoTracking()
            .Where(address => address.Id == addressId
                && address.UserId == userId
                && address.IsEnabled
                && address.DeletedOnUtc == null)
            .Select(address => new UserAddressResponse(
                address.Id,
                address.Label,
                address.RecipientName,
                address.PhoneNumber,
                address.CountryCode,
                address.Province,
                address.City,
                address.District,
                address.Street,
                address.PostalCode,
                address.IsDefault,
                address.CreatedOnUtc,
                address.UpdatedOnUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<LoyaltyPointAccountResponse> GetPointAccountAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var account = await db.LoyaltyPointAccounts
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new LoyaltyPointAccountResponse(
                candidate.Id,
                candidate.Balance,
                candidate.LifetimeEarned,
                candidate.LifetimeSpent,
                candidate.CreatedOnUtc,
                candidate.UpdatedOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
        return account ?? new LoyaltyPointAccountResponse(userId, 0, 0, 0, null, null);
    }

    public async Task<IReadOnlyList<LoyaltyPointEntryResponse>> ListPointEntriesAsync(
        string userId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await db.LoyaltyPointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.OccurredOnUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 100))
            .Select(entry => new LoyaltyPointEntryResponse(
                entry.Id,
                entry.Delta,
                entry.BalanceBefore,
                entry.BalanceAfter,
                entry.Source,
                entry.Reason,
                entry.OccurredOnUtc))
            .ToListAsync(cancellationToken);

    public Task<LoyaltyPointEntryResponse?> GetPointEntryAsync(
        string userId,
        Guid entryId,
        CancellationToken cancellationToken) =>
        db.LoyaltyPointEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.Id == entryId)
            .Select(entry => new LoyaltyPointEntryResponse(
                entry.Id,
                entry.Delta,
                entry.BalanceBefore,
                entry.BalanceAfter,
                entry.Source,
                entry.Reason,
                entry.OccurredOnUtc))
            .SingleOrDefaultAsync(cancellationToken);
}
