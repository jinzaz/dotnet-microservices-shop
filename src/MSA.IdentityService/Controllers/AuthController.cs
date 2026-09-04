using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.IdentityService.Controllers;

[ApiController]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("api/auth/register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
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
        var user = await users.FindByEmailAsync(request.Email);
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
    public async Task<IActionResult> AuthorizeEndpoint()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect request is unavailable.");
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded)
        {
            var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
            return Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, IdentityConstants.ApplicationScheme);
        }

        var user = await users.GetUserAsync(cookie.Principal!);
        if (user is null || !user.IsActive)
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return SignIn(await CreatePrincipal(user, request.GetScopes()), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpPost("~/connect/token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenID Connect request is unavailable.");

        if (request.IsPasswordGrantType())
        {
            var user = await users.FindByNameAsync(request.Username!);
            if (user is null || !user.IsActive)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var passwordResult = await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
            if (!passwordResult.Succeeded)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            user.LastLoginOnUtc = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);
            return SignIn(await CreatePrincipal(user, request.GetScopes()), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var id = result.Principal?.GetClaim(Claims.Subject);
            var user = id is null ? null : await users.FindByIdAsync(id);
            if (user is null || !user.IsActive)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            return SignIn(await CreatePrincipal(user, request.GetScopes()), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId!).SetClaim(Claims.Name, request.ClientId!);
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources("api");
            principal.SetDestinations(_ => [Destinations.AccessToken]);
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new NotSupportedException("Grant type is not supported.");
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

    private async Task<ClaimsPrincipal> CreatePrincipal(ApplicationUser user, IEnumerable<string> requestedScopes)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identity
            .SetClaim(Claims.Subject, user.Id)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Name, user.DisplayName);

        foreach (var role in await users.GetRolesAsync(user))
            identity.AddClaim(new Claim(Claims.Role, role));

        var principal = new ClaimsPrincipal(identity);
        var allowedScopes = new[]
        {
            Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess,
            "api", "catalog.write", "inventory.write", "notifications.read.all"
        };
        principal.SetScopes(requestedScopes.Intersect(allowedScopes));
        principal.SetResources("api");
        principal.SetDestinations(_ => [Destinations.AccessToken]);
        return principal;
    }

    private IActionResult IdentityValidationProblem(IdentityResult result) => ValidationProblem(
        new ValidationProblemDetails(result.Errors
            .GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray())));
}

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
