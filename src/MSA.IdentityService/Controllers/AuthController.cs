using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MSA.IdentityService.Infrastructure.Captcha;
using MSA.IdentityService.Infrastructure.Throttling;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using MSA.IdentityService.Application.Sms;
using MSA.Contracts;
using MSA.EventBusOutbox;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    IdentityThrottle throttle,
    CaptchaService captcha,
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
    [HttpGet("captcha")]
    [Throttle(ThrottlePolicy.Captcha)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<ActionResult<CaptchaResponse>> GetCaptcha() => Ok(await captcha.CreateAsync());

    [AllowAnonymous]
    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.Registration)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = SafeDisplayNameAttribute.Normalize(request.DisplayName),
            EmailConfirmed = false,
            LockoutEnabled = true,
            CreatedOnUtc = timeProvider.GetUtcNow()
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // 邮箱已注册时与注册成功返回同样的 204，注册接口不再直接回答"这个邮箱有没有注册"。
            // CreateAsync 先校验密码再查重，所以弱密码对新旧邮箱的报错也一样。
            // 没有邮箱确认流程时这只能提高探测成本：对方仍可以拿自己填的密码去登录试出来（登录也有限流）。
            var errors = result.Errors
                .Where(error => error.Code is not (nameof(IdentityErrorDescriber.DuplicateEmail)
                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
                .ToArray();
            if (errors.Length > 0)
                return IdentityValidationProblem(IdentityResult.Failed(errors));

            logger.LogInformation("Registration attempted with an email that is already registered.");
            return NoContent();
        }

        result = await users.AddToRoleAsync(user, "customer");
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        await PublishRegisteredAsync(user, "Password", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("login/password")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.Authentication)]
    public async Task<IActionResult> PasswordLogin(PasswordLoginRequest request)
    {
        // 失败按"客户端 IP + 账号"计数，不锁账号本身：攻击者只能挡住自己那个 IP，挡不住真正的用户。
        // 账号不存在时同样计数、同样提示，不暴露账号是否存在。
        var email = request.Email.Trim();
        var failurePartition = LoginFailurePartition(email);
        if (await throttle.PeekAsync(ThrottlePolicy.LoginFailures, failurePartition) is { } retryAfter)
            return IdentityThrottle.TooManyRequests(HttpContext, retryAfter);

        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
            return await FailedPasswordLogin(failurePartition);

        var result = await signIn.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: false);
        if (result.RequiresTwoFactor)
        {
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            return TwoFactorRequired();
        }
        if (!result.Succeeded)
            return await FailedPasswordLogin(failurePartition);

        await throttle.ResetAsync(ThrottlePolicy.LoginFailures, failurePartition);
        await RecordSuccessfulLogin(user);
        return NoContent();
    }

    private string LoginFailurePartition(string email)
    {
        // 键里不放明文邮箱
        var account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(users.NormalizeEmail(email)!)));
        return $"{IdentityThrottle.ClientPartition(HttpContext)}:{account[..32]}";
    }

    private async Task<IActionResult> FailedPasswordLogin(string failurePartition)
    {
        await throttle.HitAsync(ThrottlePolicy.LoginFailures, failurePartition);
        return InvalidLogin();
    }

    [AllowAnonymous]
    [HttpPost("login/sms/code")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.SmsDispatch)]
    public async Task<ActionResult<SmsLoginCodeRequestedResponse>> RequestSmsLoginCode(
        RequestSmsLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (!await captcha.VerifyAsync(request.CaptchaId, request.CaptchaAnswer))
            return InvalidCaptcha();

        await smsVerification.RequestLoginCodeAsync(request.PhoneNumber, cancellationToken);
        return Accepted(new SmsLoginCodeRequestedResponse(
            smsOptions.Value.CodeLifetimeSeconds,
            smsOptions.Value.ResendCooldownSeconds));
    }

    [AllowAnonymous]
    [HttpPost("login/sms")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.Authentication)]
    public async Task<IActionResult> SmsLogin(
        SmsLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!await smsVerification.ConsumeLoginCodeAsync(request.PhoneNumber, request.Code, cancellationToken))
            return InvalidLogin();

        // 登录当前持有这个已验证号码的账户；没有就注册一个（登录即注册）。
        var phone = request.PhoneNumber.Trim();
        var user = await FindPhoneOwnerAsync(phone, cancellationToken)
            ?? await RegisterByPhoneAsync(phone, cancellationToken)
            ?? await FindPhoneOwnerAsync(phone, cancellationToken);
        if (user is null || !user.IsActive
            || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            return InvalidLogin();
        }
        if (user.TwoFactorEnabled)
            return TwoFactorRequired();

        return await CompleteLogin(user, request.RememberMe, "sms");
    }

    [AllowAnonymous]
    [HttpPost("password/reset/code")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.SmsDispatch)]
    public async Task<ActionResult<SmsLoginCodeRequestedResponse>> RequestPasswordResetCode(
        RequestSmsLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (!await captcha.VerifyAsync(request.CaptchaId, request.CaptchaAnswer))
            return InvalidCaptcha();

        await smsVerification.RequestPasswordResetCodeAsync(request.PhoneNumber, cancellationToken);
        return Accepted(new SmsLoginCodeRequestedResponse(
            smsOptions.Value.CodeLifetimeSeconds,
            smsOptions.Value.ResendCooldownSeconds));
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    [ValidateAntiForgeryToken]
    [Throttle(ThrottlePolicy.Authentication)]
    public async Task<IActionResult> ResetPassword(
        SmsPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await smsVerification.ConsumePasswordResetCodeAsync(
            request.PhoneNumber,
            request.Code,
            cancellationToken);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive || !user.PhoneNumberConfirmed
            || !string.Equals(user.PhoneNumber, request.PhoneNumber.Trim(), StringComparison.Ordinal))
        {
            return InvalidPasswordReset();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var resetToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!result.Succeeded)
            return IdentityValidationProblem(result);

        await tokens.RevokeBySubjectAsync(user.Id, cancellationToken);
        await authorizations.RevokeBySubjectAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await signIn.SignOutAsync();
        logger.LogInformation("User {UserId} reset their password and revoked existing sessions.", user.Id);
        return NoContent();
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
                    ["returnUrl"] = returnUrl
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
    [Throttle(ThrottlePolicy.Authentication)]
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

    private Task<ApplicationUser?> FindPhoneOwnerAsync(string phone, CancellationToken cancellationToken) =>
        users.Users.FirstOrDefaultAsync(
            user => user.PhoneNumber == phone && user.PhoneNumberConfirmed,
            cancellationToken);

    /// <summary>
    /// 短信验证码已经证明持有这个号码，直接建一个手机号已验证、没有邮箱和密码的账户；之后可在资料里补邮箱，用找回密码设密码。
    /// 同一号码并发登录时 PhoneNumberIndex（已验证号码唯一）会拦下后一次插入，返回 null 让调用方重新查一次。
    /// </summary>
    private async Task<ApplicationUser?> RegisterByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = $"sms_{Guid.NewGuid():N}",
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            DisplayName = $"用户{phone[^4..]}",
            LockoutEnabled = true,
            CreatedOnUtc = timeProvider.GetUtcNow()
        };

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await users.CreateAsync(user);
            if (result.Succeeded)
                result = await users.AddToRoleAsync(user, "customer");
            if (!result.Succeeded)
            {
                logger.LogError(
                    "SMS sign-up failed: {Errors}",
                    string.Join("; ", result.Errors.Select(error => error.Code)));
                return null;
            }

            await PublishRegisteredAsync(user, "Sms", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("User {UserId} signed up with an SMS code.", user.Id);
            return user;
        }
        catch (DbUpdateException exception)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(exception, "SMS sign-up lost a race; using the account created concurrently.");
            return null;
        }
    }

    /// <summary>注册事件写进 Outbox，与建号同一事务提交：建号回滚就没有事件，建号成功事件一定会发出。</summary>
    private Task PublishRegisteredAsync(ApplicationUser user, string method, CancellationToken cancellationToken)
    {
        ((IOutboxStore)db).AddIntegrationEvent(new UserRegisteredIntegrationEvent
        {
            UserId = user.Id,
            Method = method,
            RegisteredOnUtc = user.CreatedOnUtc
        });
        return db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IActionResult> CompleteLogin(
        ApplicationUser user,
        bool rememberMe,
        string authenticationMethod)
    {
        await RecordSuccessfulLogin(user);
        await signIn.SignInAsync(user, rememberMe, authenticationMethod);
        return NoContent();
    }

    private async Task RecordSuccessfulLogin(ApplicationUser user)
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

    private ActionResult InvalidCaptcha() => ValidationProblem(new ValidationProblemDetails(
        new Dictionary<string, string[]>
        {
            ["CaptchaAnswer"] = ["图形验证码错误或已过期，请重新输入。"]
        }));

    private ObjectResult InvalidLogin() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "登录失败",
        detail: "登录凭据无效或账户暂时不可用。");

    private ObjectResult InvalidPasswordReset() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "重置失败",
        detail: "验证码无效、已过期或账户暂时不可用。");

    private ObjectResult TwoFactorRequired() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "需要双重验证",
        detail: "该账户已启用双重验证，当前登录页暂不支持第二因素，请联系管理员。");

    private IActionResult IdentityValidationProblem(IdentityResult result) => ValidationProblem(
        new ValidationProblemDetails(result.Errors
            .GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray())));
}

/// <summary>注册 Identity 账户。</summary>
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Password,
    [Required, StringLength(100), SafeDisplayName] string DisplayName);

/// <summary>
/// 显示名称会出现在后台列表、商城评价等别人能看到的地方：拒绝控制字符、换行、零宽字符、
/// RTL/LTR 方向覆盖（U+202E 之类，能把"evil"显示成"live"冒充别人）、私用区与未分配码位。
/// 代价是带零宽连接符的组合 emoji（👨‍👩‍👧）也会被拒，单个 emoji 不受影响。
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class SafeDisplayNameAttribute() : ValidationAttribute("显示名称不能为空，且不能包含换行、零宽或方向控制等不可见字符。")
{
    public static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    public override bool IsValid(object? value)
    {
        if (value is null)
            return true; // 交给 [Required]
        if (value is not string text)
            return false;

        foreach (var rune in text.EnumerateRunes())
        {
            // 不成对的代理项会被枚举成 U+FFFD；必须先排除，否则 Normalize 会抛异常
            if (rune == Rune.ReplacementChar)
                return false;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return false;
        }
        return Normalize(text).Length > 0;
    }
}

/// <summary>使用邮箱和密码登录。</summary>
public sealed record PasswordLoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password,
    bool RememberMe = false);

/// <summary>修改当前账户密码。</summary>
public sealed record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 10)] string NewPassword);

/// <summary>由客户端回传到 X-XSRF-TOKEN 请求头的防伪令牌。</summary>
public sealed record AntiforgeryTokenResponse(string Token);
