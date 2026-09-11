namespace MSA.BFF.Web.Auth;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;

public sealed class RedisTicketStore : ITicketStore
{
    private const string KeyPrefix = "session:";
    internal const string SessionKeyProperty = ".bff.session_key";

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;

    public RedisTicketStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider)
    {
        _cache = cache;

        _protector = dataProtectionProvider.CreateProtector(
            "MSA.Web.BFF.RedisTicketStore.v1");
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var sessionId = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));

        var key = KeyPrefix + sessionId;
        ticket.Properties.Items[SessionKeyProperty] = key;

        await StoreTicketAsync(key, ticket, cancellationToken);

        return key;
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket) =>
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
            // Redis 中保存的内容也是加密的。
            var unprotected = _protector.Unprotect(data);

            return TicketSerializer.Default.Deserialize(unprotected);
        }
        catch (CryptographicException)
        {
            // DataProtection Key 不匹配或数据损坏。
            await _cache.RemoveAsync(key, cancellationToken);

            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(key, cancellationToken);

    private async Task StoreTicketAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var serialized =
            TicketSerializer.Default.Serialize(ticket);

        // Redis 里不要直接明文保存 AccessToken / RefreshToken。
        var protectedData =
            _protector.Protect(serialized);

        var options =
            new DistributedCacheEntryOptions();

        if (ticket.Properties.ExpiresUtc is { } expiresUtc)
        {
            options.AbsoluteExpiration = expiresUtc;
        }
        else
        {
            options.AbsoluteExpirationRelativeToNow =
                TimeSpan.FromHours(8);
        }

        await _cache.SetAsync(
            key,
            protectedData,
            options,
            cancellationToken);
    }
}
