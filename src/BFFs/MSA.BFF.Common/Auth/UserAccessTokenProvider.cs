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
    // ponytail: one process-wide lock; use a distributed per-session lock when multiple BFF replicas refresh concurrently.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

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
        await RefreshLock.WaitAsync(cancellationToken);
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
            RefreshLock.Release();
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
