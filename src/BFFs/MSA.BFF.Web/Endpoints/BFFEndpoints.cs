using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using MSA.BFF.Web.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.BFF.Web.Endpoints;

internal static class BFFEndpoints
{
    internal static IEndpointRouteBuilder MapBFFEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var bffGroup = endpoints.MapGroup("/api/bff")
            .WithTags("bff");

        bffGroup.MapGet("/csrf", GetCSRF)
            .AllowAnonymous()
            .WithName("GetCSRF")
            .WithSummary("Retrieve the CSRF token for the current session.")
            .Produces<CSRFResponse>()
            .Produces(StatusCodes.Status204NoContent);

        bffGroup.MapGet("/login", Login)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Start the authorization code login flow.")
            .Produces(StatusCodes.Status302Found);

        bffGroup.MapPost("/logout", Logout)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("Clear the local session and start the identity-provider logout flow.")
            .Produces(StatusCodes.Status302Found);

        bffGroup.MapMethods("/callback/login", [HttpMethods.Get, HttpMethods.Post], LoginCallback)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithName("LoginCallback")
            .WithSummary("Complete the authorization code login flow.");

        bffGroup.MapMethods("/callback/logout", [HttpMethods.Get, HttpMethods.Post], LogoutCallback)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithName("LogoutCallback")
            .WithSummary("Complete the identity-provider logout flow.");

        bffGroup.MapGet("/user", (Delegate)GetUserAsync)
            .AllowAnonymous()
            .WithName("GetUserSession")
            .WithSummary("Retrieve the user represented by the current BFF session.")
            .Produces<UserSessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static Results<NoContent, Ok<CSRFResponse>> GetCSRF(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var token = antiforgery.GetAndStoreTokens(context).RequestToken;

        return string.IsNullOrEmpty(token)
            ? TypedResults.NoContent()
            : TypedResults.Ok(new CSRFResponse(token));
    }

    private static IResult Login(
        IConfiguration configuration,
        string? returnUrl)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = GetFrontendReturnUrl(configuration, returnUrl)
        };

        return TypedResults.Challenge(
            properties,
            [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        IConfiguration configuration,
        string? returnUrl)
    {
        var redirectUri = GetFrontendReturnUrl(configuration, returnUrl);
        var result = await context.AuthenticateAsync(ServiceCollections.BffCookie);

        if (result is not { Succeeded: true, Properties: not null })
            return TypedResults.Redirect(redirectUri);

        await context.SignOutAsync(ServiceCollections.BffCookie);

        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictClientAspNetCoreConstants.Properties.IdentityTokenHint] =
                result.Properties.GetTokenValue(
                    OpenIddictClientAspNetCoreConstants.Tokens.BackchannelIdentityToken)
        })
        {
            RedirectUri = redirectUri
        };

        return TypedResults.SignOut(
            properties,
            [OpenIddictClientAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> LoginCallback(
        HttpContext context,
        IConfiguration configuration)
    {
        var result = await context.AuthenticateAsync(
            OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        if (result is not
            {
                Succeeded: true,
                Principal.Identity: { IsAuthenticated: true },
                Properties: not null
            })
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "The external authentication response is invalid.");
        }

        var identity = new ClaimsIdentity(
            ServiceCollections.BffCookie,
            Claims.Name,
            Claims.Role);
        identity
            .SetClaim(Claims.Subject, result.Principal.GetClaim(Claims.Subject))
            .SetClaim(Claims.Name, result.Principal.GetClaim(Claims.Name))
            .SetClaim(Claims.Email, result.Principal.GetClaim(Claims.Email))
            .SetClaims(Claims.Role, result.Principal.GetClaims(Claims.Role))
            .SetClaim(
                Claims.Private.RegistrationId,
                result.Principal.GetClaim(Claims.Private.RegistrationId))
            .SetClaim(
                Claims.Private.ProviderName,
                result.Principal.GetClaim(Claims.Private.ProviderName));

        var properties = new AuthenticationProperties(result.Properties.Items)
        {
            RedirectUri = GetSafeCallbackReturnUrl(configuration, result.Properties.RedirectUri),
            IssuedUtc = null,
            ExpiresUtc = null,
            IsPersistent = true
        };

        properties.StoreTokens(result.Properties.GetTokens().Where(token => token.Name is
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken or
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessTokenExpirationDate or
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelIdentityToken or
            OpenIddictClientAspNetCoreConstants.Tokens.RefreshToken));

        return TypedResults.SignIn(
            new ClaimsPrincipal(identity),
            properties,
            ServiceCollections.BffCookie);
    }

    private static async Task<IResult> LogoutCallback(
        HttpContext context,
        IConfiguration configuration)
    {
        var result = await context.AuthenticateAsync(
            OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        return TypedResults.Redirect(
            GetSafeCallbackReturnUrl(configuration, result?.Properties?.RedirectUri));
    }

    private static async Task<Results<Ok<UserSessionResponse>, ProblemHttpResult>> GetUserAsync(
        HttpContext context)
    {
        var result = await context.AuthenticateAsync(ServiceCollections.BffCookie);

        if (result is not { Succeeded: true, Principal: not null })
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No active BFF session was found.");
        }

        var principal = result.Principal;
        var roleClaimType = (principal.Identity as ClaimsIdentity)?.RoleClaimType;
        var roles = principal.Claims
            .Where(claim => claim.Type == Claims.Role ||
                            claim.Type == ClaimTypes.Role ||
                            claim.Type == roleClaimType)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return TypedResults.Ok(new UserSessionResponse(
            IsAuthenticated: true,
            Subject: principal.GetClaim(Claims.Subject) ??
                     principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Name: principal.Identity?.Name ??
                  principal.GetClaim(Claims.Name) ??
                  principal.FindFirst(ClaimTypes.Name)?.Value,
            Email: principal.GetClaim(Claims.Email) ??
                   principal.FindFirst(ClaimTypes.Email)?.Value,
            Roles: roles));
    }

    private static string GetFrontendReturnUrl(
        IConfiguration configuration,
        string? returnUrl)
    {
        var path = IsStrictLocalUrl(returnUrl) ? returnUrl! : "/";
        return new Uri(GetFrontendBaseUri(configuration), path).AbsoluteUri;
    }

    private static string GetSafeCallbackReturnUrl(
        IConfiguration configuration,
        string? returnUrl)
    {
        var frontendBaseUri = GetFrontendBaseUri(configuration);

        if (IsStrictLocalUrl(returnUrl))
            return new Uri(frontendBaseUri, returnUrl!).AbsoluteUri;

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var candidate) &&
            IsSameOrigin(frontendBaseUri, candidate))
        {
            return candidate.AbsoluteUri;
        }

        return frontendBaseUri.AbsoluteUri;
    }

    private static bool IsStrictLocalUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url[0] == '/' &&
        RedirectHttpResult.IsLocalUrl(url);

    private static Uri GetFrontendBaseUri(IConfiguration configuration)
    {
        var value = configuration["Bff:FrontendBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "Bff:FrontendBaseUrl must be an HTTP(S) origin without a path, query or fragment.");
        }

        return uri;
    }

    private static bool IsSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port &&
        string.IsNullOrEmpty(right.UserInfo);
}

/// <summary>Contains the antiforgery request token for the current BFF session.</summary>
public sealed record CSRFResponse(string Token);

/// <summary>Describes the user represented by the current BFF session.</summary>
public sealed record UserSessionResponse(
    bool IsAuthenticated,
    string? Subject,
    string? Name,
    string? Email,
    IReadOnlyList<string> Roles);
