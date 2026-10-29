using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.BFF.Common.Auth;

internal sealed class UserAccessTokenProvider(
    OpenIddictClientService client,
    RedisTicketStore tickets)
{
    // 按会话分段加锁：同一会话的刷新必须串行（刷新令牌会轮换，并发刷新会让后一次拿着已作废的令牌失败），
    // 不同会话互不阻塞。原来是一把全进程锁，认证中心刷新一次约 300ms，全站每秒只能刷 3 个令牌，
    // 令牌集中过期时请求在锁前排队二十多秒。
    // ponytail: 进程内锁；多个 BFF 副本同时刷新同一会话时需要按会话的分布式锁。
    private static readonly SemaphoreSlim[] RefreshLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim RefreshLockFor(string sessionKey) =>
        RefreshLocks[(int)((uint)StringComparer.Ordinal.GetHashCode(sessionKey) % (uint)RefreshLocks.Length)];

    internal async Task<string?> GetAccessTokenAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var authentication = await context.AuthenticateAsync(BffDefaults.CookieScheme);
        if (authentication is not
            {
                Succeeded: true,
                Principal: not null,
                Properties: not null
            })
        {
            return null;
        }

        if (TryGetUsableAccessToken(authentication.Properties, out var accessToken))
            return accessToken;

        if (!authentication.Properties.Items.TryGetValue(
                RedisTicketStore.SessionKeyProperty,
                out var sessionKey) ||
            string.IsNullOrWhiteSpace(sessionKey))
        {
            await context.SignOutAsync(BffDefaults.CookieScheme);
            return null;
        }

        try
        {
            return await RefreshAsync(context, sessionKey, cancellationToken);
        }
        catch (OpenIddictExceptions.ProtocolException)
        {
            await context.SignOutAsync(BffDefaults.CookieScheme);
            return null;
        }
    }

    private async Task<string?> RefreshAsync(
        HttpContext context,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        var refreshLock = RefreshLockFor(sessionKey);
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var ticket = await tickets.RetrieveAsync(sessionKey, cancellationToken);
            if (ticket is null)
            {
                await context.SignOutAsync(BffDefaults.CookieScheme);
                return null;
            }

            if (TryGetUsableAccessToken(ticket.Properties, out var accessToken))
            {
                await context.SignInAsync(
                    BffDefaults.CookieScheme,
                    ticket.Principal,
                    ticket.Properties);
                return accessToken;
            }

            var refreshToken = ticket.Properties.GetTokenValue(
                OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken);
            var registrationId = ticket.Principal.GetClaim(Claims.Private.RegistrationId);
            if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(registrationId))
            {
                await context.SignOutAsync(BffDefaults.CookieScheme);
                return null;
            }

            var refreshed = await client.AuthenticateWithRefreshTokenAsync(
                new OpenIddictClientModels.RefreshTokenAuthenticationRequest
                {
                    RegistrationId = registrationId,
                    RefreshToken = refreshToken,
                    CancellationToken = cancellationToken
                });

            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
            {
                await context.SignOutAsync(BffDefaults.CookieScheme);
                return null;
            }

            var properties = ticket.Properties.Clone();
            properties.RedirectUri = null;
            properties.UpdateTokenValue(
                OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken,
                refreshed.AccessToken);
            properties.UpdateTokenValue(
                OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessTokenExpirationDate,
                refreshed.AccessTokenExpirationDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(refreshed.IdentityToken))
            {
                properties.UpdateTokenValue(
                    OpenIddictClientAspNetCoreConstants.Tokens.BackchannelIdentityToken,
                    refreshed.IdentityToken);
            }

            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                properties.UpdateTokenValue(
                    OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken,
                    refreshed.RefreshToken);
            }

            await context.SignInAsync(
                BffDefaults.CookieScheme,
                ticket.Principal,
                properties);

            return refreshed.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static bool TryGetUsableAccessToken(
        AuthenticationProperties properties,
        out string? accessToken)
    {
        accessToken = properties.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken);
        var expiration = properties.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessTokenExpirationDate);

        return !string.IsNullOrWhiteSpace(accessToken) &&
               DateTimeOffset.TryParse(
                   expiration,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal,
                   out var expiresAt) &&
               expiresAt > DateTimeOffset.UtcNow.AddSeconds(30);
    }
}
