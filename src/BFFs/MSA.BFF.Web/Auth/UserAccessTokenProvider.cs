namespace MSA.BFF.Web.Auth;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MSA.BFF.Web.Auth.Abstractions;
using OpenIddict.Client.AspNetCore;

public sealed class UserAccessTokenProvider
    : IUserAccessTokenProvider
{
    public async Task<string?> GetAccessTokenAsync(
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var result =
            await context.AuthenticateAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        if (!result.Succeeded)
            return null;

        return result.Properties?.GetTokenValue(
            OpenIddictClientAspNetCoreConstants
                .Tokens
                .BackchannelAccessToken);
    }
}
