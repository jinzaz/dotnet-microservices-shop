using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.IdentityService.Controllers;

[ApiController]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictScopeManager scopes,
    TimeProvider timeProvider) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("api/auth/register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = false
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        await users.AddToRoleAsync(user, "customer");
        return Created($"/api/users/{user.Id}", new { user.Id, user.Email, user.DisplayName });
    }

    [AllowAnonymous]
    [HttpPost("api/auth/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return Unauthorized();

        var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized();

        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeEndpoint(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = authentication.Succeeded
            ? await users.GetUserAsync(authentication.Principal!)
            : null;
        var issuedUtc = authentication.Properties?.IssuedUtc;
        var exceedsMaxAge = request.MaxAge is { } maxAge &&
            (issuedUtc is null ||
             timeProvider.GetUtcNow() - issuedUtc.Value > TimeSpan.FromSeconds(maxAge));

        if (user is null || !user.IsActive || !await signIn.CanSignInAsync(user) ||
            request.HasPromptValue(PromptValues.Login) || exceedsMaxAge)
        {
            if (authentication.Succeeded)
                await signIn.SignOutAsync();

            if (request.HasPromptValue(PromptValues.None))
                return OAuthForbid(Errors.LoginRequired, "The user is not logged in.");

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = await GetAuthorizationReturnUrl(request, cancellationToken)
                },
                IdentityConstants.ApplicationScheme);
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The calling client application cannot be found.");
        var applicationId = await applications.GetIdAsync(application, cancellationToken)
            ?? throw new InvalidOperationException("The calling client identifier cannot be resolved.");

        var grants = await authorizations.FindAsync(
            subject: user.Id,
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes(),
            cancellationToken).ToListAsync(cancellationToken);

        var consentType = await applications.GetConsentTypeAsync(application, cancellationToken);
        var canIssue = consentType switch
        {
            ConsentTypes.Implicit => true,
            ConsentTypes.External => grants.Count > 0,
            ConsentTypes.Explicit => grants.Count > 0 && !request.HasPromptValue(PromptValues.Consent),
            _ => false
        };

        if (!canIssue)
            return OAuthForbid(Errors.ConsentRequired, "Interactive user consent is required.");

        var principal = await CreatePrincipal(user, request.GetScopes(), cancellationToken);
        var authorization = grants.LastOrDefault() ?? await authorizations.CreateAsync(
            (ClaimsIdentity)principal.Identity!,
            user.Id,
            applicationId,
            AuthorizationTypes.Permanent,
            principal.GetScopes(),
            cancellationToken);
        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpPost("~/connect/token")]
    [Consumes("application/x-www-form-urlencoded")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var authentication = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = authentication.Principal?.GetClaim(Claims.Subject);
            var user = subject is null ? null : await users.FindByIdAsync(subject);

            if (user is null || !user.IsActive ||
                !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            {
                return OAuthForbid(Errors.InvalidGrant, "The token is no longer valid.");
            }

            var identity = new ClaimsIdentity(
                authentication.Principal!.Claims,
                TokenValidationParameters.DefaultAuthenticationType,
                Claims.Name,
                Claims.Role);
            await SetUserClaims(identity, user);
            identity.SetDestinations(GetDestinations);

            return SignIn(
                new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // OpenIddict validates the client identifier, secret and permissions before pass-through.
            var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
                ?? throw new InvalidOperationException("The calling client application cannot be found.");
            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                Claims.Name,
                Claims.Role);
            identity
                .SetClaim(Claims.Subject, await applications.GetClientIdAsync(application, cancellationToken))
                .SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application, cancellationToken));

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(await scopes.ListResourcesAsync(principal.GetScopes()).ToListAsync(cancellationToken));
            principal.SetDestinations(GetDestinations);

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    [AllowAnonymous]
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize]
    [HttpPost("api/auth/change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded ? NoContent() : IdentityValidationProblem(result);
    }

    private async Task<ClaimsPrincipal> CreatePrincipal(
        ApplicationUser user,
        IEnumerable<string> requestedScopes,
        CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        await SetUserClaims(identity, user);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(requestedScopes);
        principal.SetResources(await scopes.ListResourcesAsync(principal.GetScopes()).ToListAsync(cancellationToken));
        principal.SetDestinations(GetDestinations);
        return principal;
    }

    private async Task SetUserClaims(ClaimsIdentity identity, ApplicationUser user) => identity
        .SetClaim(Claims.Subject, user.Id)
        .SetClaim(Claims.Email, user.Email)
        .SetClaim(
            Claims.Name,
            !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.UserName ?? user.Id)
        .SetClaim(Claims.PreferredUsername, user.UserName)
        .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);

    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Name or Claims.PreferredUsername when claim.Subject!.HasScope(Scopes.Profile) =>
            [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Email when claim.Subject!.HasScope(Scopes.Email) =>
            [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Role when claim.Subject!.HasScope(Scopes.Roles) =>
            [Destinations.AccessToken, Destinations.IdentityToken],
        _ => [Destinations.AccessToken]
    };

    private async Task<string> GetAuthorizationReturnUrl(
        OpenIddictRequest request,
        CancellationToken cancellationToken)
    {
        IEnumerable<KeyValuePair<string, StringValues>> parameters = Request.HasFormContentType
            ? await Request.ReadFormAsync(cancellationToken)
            : Request.Query;

        if (request.HasPromptValue(PromptValues.Login))
        {
            var values = parameters
                .Where(parameter => !string.Equals(
                    parameter.Key,
                    Parameters.Prompt,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var prompt = string.Join(' ', request.GetPromptValues()
                .Where(value => value != PromptValues.Login));

            if (!string.IsNullOrEmpty(prompt))
                values.Add(new KeyValuePair<string, StringValues>(Parameters.Prompt, prompt));

            parameters = values;
        }

        return Request.PathBase + Request.Path + QueryString.Create(parameters);
    }

    private IActionResult OAuthForbid(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private IActionResult IdentityValidationProblem(IdentityResult result) => ValidationProblem(
        new ValidationProblemDetails(result.Errors
            .GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray())));
}

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required] string Password,
    [Required, StringLength(100)] string DisplayName);

public sealed record LoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required] string Password,
    bool RememberMe = false);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);
