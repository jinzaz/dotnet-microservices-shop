using MSA.Infrastrucute.Entities;

namespace MSA.IdentityService.Domain;

public sealed class UserAddress : AggregateRoot<Guid>
{
    public const int MaximumActiveAddressesPerUser = 20;

    private UserAddress()
    {
    }

    private UserAddress(
        Guid id,
        string userId,
        AddressDetails details,
        bool isDefault,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Address id is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Id = id;
        UserId = userId.Trim();
        Apply(details);
        IsDefault = isDefault;
        IsEnabled = true;
        CreatedOnUtc = now;
        UpdatedOnUtc = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public string Province { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string District { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public string? PostalCode { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public DateTimeOffset? DeletedOnUtc { get; private set; }
    public long Version { get; private set; }

    public static UserAddress Create(
        Guid id,
        string userId,
        AddressDetails details,
        bool isDefault,
        DateTimeOffset now) =>
        new(id, userId, details, isDefault, now);

    public void Update(AddressDetails details, DateTimeOffset now)
    {
        EnsureActive();
        Apply(details);
        Touch(now);
    }

    public void MakeDefault(DateTimeOffset now)
    {
        EnsureActive();
        if (IsDefault)
            return;

        IsDefault = true;
        Touch(now);
    }

    public void Remove(DateTimeOffset now)
    {
        EnsureActive();
        IsDefault = false;
        IsEnabled = false;
        DeletedOnUtc = now;
        Touch(now);
    }

    private void Apply(AddressDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Label = Required(details.Label, nameof(details.Label), 50);
        RecipientName = Required(details.RecipientName, nameof(details.RecipientName), 100);
        PhoneNumber = Required(details.PhoneNumber, nameof(details.PhoneNumber), 32);
        CountryCode = Required(details.CountryCode, nameof(details.CountryCode), 2).ToUpperInvariant();
        Province = Required(details.Province, nameof(details.Province), 100);
        City = Required(details.City, nameof(details.City), 100);
        District = Required(details.District, nameof(details.District), 100);
        Street = Required(details.Street, nameof(details.Street), 300);
        PostalCode = Optional(details.PostalCode, nameof(details.PostalCode), 20);
    }

    private void EnsureActive()
    {
        if (!IsEnabled || DeletedOnUtc is not null)
            throw new IdentityBusinessException("The address is no longer active.");
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedOnUtc = now;
        Version++;
    }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }

    private static string? Optional(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        return normalized;
    }
}

public sealed record AddressDetails(
    string Label,
    string RecipientName,
    string PhoneNumber,
    string CountryCode,
    string Province,
    string City,
    string District,
    string Street,
    string? PostalCode);
