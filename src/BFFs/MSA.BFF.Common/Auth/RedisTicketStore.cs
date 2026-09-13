using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;

namespace MSA.BFF.Common.Auth;

internal sealed class RedisTicketStore : ITicketStore
{
    private const string KeyPrefix = "session:";
    internal const string SessionKeyProperty = ".bff.session_key";

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;

    public RedisTicketStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider,
        BffApplicationOptions application)
    {
        _cache = cache;
        _protector = dataProtectionProvider.CreateProtector(application.TicketProtectionPurpose);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var key = KeyPrefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        ticket.Properties.Items[SessionKeyProperty] = key;
        await StoreTicketAsync(key, ticket, cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        ticket.Properties.Items[SessionKeyProperty] = key;
        return StoreTicketAsync(key, ticket, cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var data = await _cache.GetAsync(key, cancellationToken);
        if (data is null)
            return null;

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(data));
        }
        catch (CryptographicException)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(key, cancellationToken);

    private Task StoreTicketAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = ticket.Properties.ExpiresUtc,
            AbsoluteExpirationRelativeToNow = ticket.Properties.ExpiresUtc is null
                ? TimeSpan.FromHours(8)
                : null
        };

        return _cache.SetAsync(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            options,
            cancellationToken);
    }
}
