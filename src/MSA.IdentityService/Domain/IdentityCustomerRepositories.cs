namespace MSA.IdentityService.Domain;

public interface IUserAddressRepository
{
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);
    Task<int> CountActiveAsync(string userId, CancellationToken cancellationToken);
    Task<UserAddress?> GetAsync(string userId, Guid addressId, CancellationToken cancellationToken);
    Task ClearDefaultAsync(string userId, DateTimeOffset changedOnUtc, CancellationToken cancellationToken);
    void Add(UserAddress address);
}

public interface ILoyaltyPointRepository
{
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken);
    Task<LoyaltyPointAccount?> GetAccountAsync(string userId, CancellationToken cancellationToken);
    Task<LoyaltyPointEntry?> GetEntryAsync(string userId, string businessKey, CancellationToken cancellationToken);
    void AddAccount(LoyaltyPointAccount account);
    void AddEntry(LoyaltyPointEntry entry);
}

public interface IUserBusinessLock
{
    Task AcquireAsync(string userId, string purpose, CancellationToken cancellationToken);
}
