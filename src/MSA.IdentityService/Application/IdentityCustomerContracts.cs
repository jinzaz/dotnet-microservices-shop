using MSA.IdentityService.Domain;

namespace MSA.IdentityService.Application;

/// <summary>Represents one user delivery address.</summary>
public sealed record UserAddressResponse(
    Guid Id,
    string Label,
    string RecipientName,
    string PhoneNumber,
    string CountryCode,
    string Province,
    string City,
    string District,
    string Street,
    string? PostalCode,
    bool IsDefault,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc);

/// <summary>Represents a user's loyalty point account.</summary>
public sealed record LoyaltyPointAccountResponse(
    string UserId,
    long Balance,
    long LifetimeEarned,
    long LifetimeSpent,
    DateTimeOffset? CreatedOnUtc,
    DateTimeOffset? UpdatedOnUtc);

/// <summary>Represents an immutable loyalty point ledger entry.</summary>
public sealed record LoyaltyPointEntryResponse(
    Guid Id,
    long Delta,
    long BalanceBefore,
    long BalanceAfter,
    string Source,
    string Reason,
    DateTimeOffset OccurredOnUtc);

public sealed record PointAdjustmentResult(LoyaltyPointEntryResponse Entry, bool IsReplay);

public interface IIdentityCustomerReadStore
{
    Task<IReadOnlyList<UserAddressResponse>> ListAddressesAsync(string userId, CancellationToken cancellationToken);
    Task<UserAddressResponse?> GetAddressAsync(string userId, Guid addressId, CancellationToken cancellationToken);
    Task<LoyaltyPointAccountResponse> GetPointAccountAsync(string userId, CancellationToken cancellationToken);
    Task<LoyaltyPointEntryResponse?> GetPointEntryAsync(
        string userId,
        Guid entryId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<LoyaltyPointEntryResponse>> ListPointEntriesAsync(
        string userId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}

internal static class IdentityCustomerResponseMapper
{
    public static UserAddressResponse ToResponse(UserAddress address) => new(
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
        address.UpdatedOnUtc);

    public static LoyaltyPointEntryResponse ToResponse(LoyaltyPointEntry entry) => new(
        entry.Id,
        entry.Delta,
        entry.BalanceBefore,
        entry.BalanceAfter,
        entry.Source,
        entry.Reason,
        entry.OccurredOnUtc);
}
