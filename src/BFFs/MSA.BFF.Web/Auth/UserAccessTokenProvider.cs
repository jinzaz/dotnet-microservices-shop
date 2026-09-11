namespace MSA.BFF.Web.Auth;

using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using MSA.BFF.Web.Auth.Abstractions;
using MSA.BFF.Web.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

public sealed class UserAccessTokenProvider(
    OpenIddictClientService client,
    RedisTicketStore tickets) : IUserAccessTokenProvider
{
    // ponytail: one process-wide lock; use a distributed per-session lock when multiple BFF replicas refresh concurrently.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    public async Task<string?> GetAccessTokenAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var authentication = await context.AuthenticateAsync(ServiceCollections.BffCookie);
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
            await context.SignOutAsync(ServiceCollections.BffCookie);
            return null;
        }

        try
        {
            return await RefreshAsync(
                context,
                sessionKey,
                cancellationToken);
        }
        catch (OpenIddictExceptions.ProtocolException)
        {
            await context.SignOutAsync(ServiceCollections.BffCookie);
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
                await context.SignOutAsync(ServiceCollections.BffCookie);
                return null;
            }

            if (TryGetUsableAccessToken(ticket.Properties, out var accessToken))
            {
                await context.SignInAsync(
                    ServiceCollections.BffCookie,
                    ticket.Principal,
                    ticket.Properties);
                return accessToken;
            }

            var refreshToken = ticket.Properties.GetTokenValue(
                OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken);
            var registrationId = ticket.Principal.GetClaim(Claims.Private.RegistrationId);
            if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(registrationId))
            {
                await context.SignOutAsync(ServiceCollections.BffCookie);
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
                await context.SignOutAsync(ServiceCollections.BffCookie);
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
                ServiceCollections.BffCookie,
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
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        var expiration = properties.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessTokenExpirationDate);
        if (string.IsNullOrWhiteSpace(expiration))
            return false;

        return DateTimeOffset.TryParse(
                   expiration,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal,
                   out var expiresAt) &&
               expiresAt > DateTimeOffset.UtcNow.AddSeconds(30);
    }
}
