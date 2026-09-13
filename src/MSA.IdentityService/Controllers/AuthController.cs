using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using MSA.IdentityService.Application.Sms;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MSA.IdentityService.Controllers;

[ApiController]
[Route("Auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    IOpenIddictScopeManager scopes,
    IAntiforgery antiforgery,
    ISmsVerificationService smsVerification,
    IOptions<SmsOptions> smsOptions,
    IConfiguration configuration,
    ILogger<AuthController> logger,
    TimeProvider timeProvider) : ControllerBase
{
    private const string ConsentDecisionParameter = "consent";
    private const string ApproveConsent = "approve";
    private const string DenyConsent = "deny";

    [AllowAnonymous]
    [HttpGet("csrf")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public ActionResult<AntiforgeryTokenResponse> GetAntiforgeryToken()
    {
        var tokenSet = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryTokenResponse(tokenSet.RequestToken!));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.Registration)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.DisplayName)] = ["显示名称不能为空。"]
            }));

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = false,
            LockoutEnabled = true,
            CreatedOnUtc = timeProvider.GetUtcNow()
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        result = await users.AddToRoleAsync(user, "customer");
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        await transaction.CommitAsync(cancellationToken);
        return Created(
            $"/Auth/users/{user.Id}",
            new RegisteredUserResponse(user.Id, user.Email!, user.DisplayName));
    }

    [AllowAnonymous]
    [HttpPost("login/password")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.Authentication)]
    public async Task<IActionResult> PasswordLogin(PasswordLoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
            return InvalidLogin();

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return InvalidLogin();

        return await CompleteLogin(user, request.RememberMe, "pwd");
    }

    [AllowAnonymous]
    [HttpPost("login/sms/code")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.SmsDispatch)]
    public async Task<ActionResult<SmsLoginCodeRequestedResponse>> RequestSmsLoginCode(
        RequestSmsLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        await smsVerification.RequestLoginCodeAsync(request.PhoneNumber, cancellationToken);
        return Accepted(new SmsLoginCodeRequestedResponse(
            smsOptions.Value.CodeLifetimeSeconds,
            smsOptions.Value.ResendCooldownSeconds));
    }

    [AllowAnonymous]
    [HttpPost("login/sms")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.Authentication)]
    public async Task<IActionResult> SmsLogin(
        SmsLoginRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await smsVerification.ConsumeLoginCodeAsync(
            request.PhoneNumber,
            request.Code,
            cancellationToken);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive || !user.PhoneNumberConfirmed
            || !string.Equals(user.PhoneNumber, request.PhoneNumber.Trim(), StringComparison.Ordinal)
            || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            return InvalidLogin();
        }

        return await CompleteLogin(user, request.RememberMe, "sms");
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutSession()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> AuthorizeEndpoint(CancellationToken cancellationToken) =>
        HandleAuthorizationAsync(consentDecision: null, cancellationToken);

    [AllowAnonymous]
    [HttpPost("~/connect/authorize")]
    [Consumes("application/x-www-form-urlencoded")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AuthorizeEndpointPost(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        var consentDecision = form[ConsentDecisionParameter].ToString();
        if (consentDecision is not (ApproveConsent or DenyConsent))
            return BadRequest(new ProblemDetails { Title = "A valid consent decision is required." });

        return await HandleAuthorizationAsync(consentDecision, cancellationToken);
    }

    private async Task<IActionResult> HandleAuthorizationAsync(
        string? consentDecision,
        CancellationToken cancellationToken)
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

        if (user is null || !user.IsActive || !await signIn.CanSignInAsync(user)
            || await users.IsLockedOutAsync(user) ||
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

        if (consentDecision == DenyConsent)
            return OAuthForbid(Errors.AccessDenied, "The resource owner denied the authorization request.");

        var consentType = await applications.GetConsentTypeAsync(application, cancellationToken);
        var canIssue = consentType switch
        {
            ConsentTypes.Implicit => true,
            ConsentTypes.External => grants.Count > 0,
            ConsentTypes.Explicit => consentDecision == ApproveConsent ||
                                     grants.Count > 0 && !request.HasPromptValue(PromptValues.Consent),
            ConsentTypes.Systematic => consentDecision == ApproveConsent,
            _ => false
        };

        if (!canIssue)
        {
            if (request.HasPromptValue(PromptValues.None) ||
                consentType is not (ConsentTypes.Explicit or ConsentTypes.Systematic))
            {
                return OAuthForbid(Errors.ConsentRequired, "Interactive user consent is required.");
            }

            var returnUrl = await GetAuthorizationReturnUrl(request, cancellationToken);
            var consentUrl = QueryHelpers.AddQueryString(
                new Uri(new Uri(configuration["IdentityUi:BaseUrl"]!), "consent").AbsoluteUri,
                new Dictionary<string, string?>
                {
                    ["returnUrl"] = returnUrl,
                    ["clientId"] = request.ClientId,
                    ["clientName"] = await applications.GetDisplayNameAsync(application, cancellationToken)
                        ?? request.ClientId,
                    ["scopes"] = string.Join(' ', request.GetScopes())
                });
            return Redirect(consentUrl);
        }

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
    [HttpPost("change-password")]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.Authentication)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return InvalidLogin();

        if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["PasswordReuse"] = ["新密码不能与当前密码相同。"]
            }));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        await tokens.RevokeBySubjectAsync(user.Id, cancellationToken);
        await authorizations.RevokeBySubjectAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await signIn.SignOutAsync();
        logger.LogInformation("User {UserId} changed their password and revoked existing sessions.", user.Id);
        return NoContent();
    }

    private async Task<IActionResult> CompleteLogin(
        ApplicationUser user,
        bool rememberMe,
        string authenticationMethod)
    {
        user.LastLoginOnUtc = timeProvider.GetUtcNow();
        var updateResult = await users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            logger.LogWarning(
                "Could not update the last-login timestamp for user {UserId}: {ErrorCodes}.",
                user.Id,
                string.Join(',', updateResult.Errors.Select(error => error.Code)));
        }

        await signIn.SignInAsync(user, rememberMe, authenticationMethod);
        return NoContent();
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

        parameters = parameters.Where(parameter =>
            !string.Equals(parameter.Key, ConsentDecisionParameter, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parameter.Key, "__RequestVerificationToken", StringComparison.OrdinalIgnoreCase));

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

    private ObjectResult InvalidLogin() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "登录失败",
        detail: "登录凭据无效或账户暂时不可用。");

    private IActionResult IdentityValidationProblem(IdentityResult result) => ValidationProblem(
        new ValidationProblemDetails(result.Errors
            .GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray())));
}

/// <summary>注册 Identity 账户。</summary>
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Password,
    [Required, StringLength(100)] string DisplayName);

/// <summary>使用邮箱和密码登录。</summary>
public sealed record PasswordLoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password,
    bool RememberMe = false);

/// <summary>修改当前账户密码。</summary>
public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword);

/// <summary>注册成功后的账户摘要。</summary>
public sealed record RegisteredUserResponse(string Id, string Email, string DisplayName);

/// <summary>由客户端回传到 X-XSRF-TOKEN 请求头的防伪令牌。</summary>
public sealed record AntiforgeryTokenResponse(string Token);
