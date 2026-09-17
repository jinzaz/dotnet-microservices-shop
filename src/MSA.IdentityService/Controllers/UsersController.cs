using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.IdentityService.Application.Sms;

namespace MSA.IdentityService.Controllers;

[Authorize]
[ApiController]
[Route("Auth/users")]
public sealed class UsersController(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    ISmsVerificationService smsVerification,
    IOptions<SmsOptions> smsOptions) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.IsActive,
            user.CreatedOnUtc,
            Roles = await users.GetRolesAsync(user)
        });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.DisplayName)] = ["显示名称不能为空。"]
            }));
        }

        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : request.PhoneNumber.Trim();
        user.DisplayName = request.DisplayName.Trim();
        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = false;
        }

        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [HttpPost("me/phone/confirmation/code")]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.SmsDispatch)]
    public async Task<ActionResult<SmsLoginCodeRequestedResponse>> RequestPhoneConfirmationCode(
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(user.PhoneNumber) || user.PhoneNumberConfirmed)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(user.PhoneNumber)] = ["请先保存一个尚未验证的手机号。"]
            }));
        }
        if (await users.Users.AnyAsync(
                candidate => candidate.Id != user.Id
                    && candidate.PhoneNumber == user.PhoneNumber
                    && candidate.PhoneNumberConfirmed,
                cancellationToken))
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(user.PhoneNumber)] = ["该手机号已绑定其他账户。"]
            }));
        }

        await smsVerification.RequestPhoneConfirmationCodeAsync(
            user.Id,
            user.PhoneNumber,
            cancellationToken);
        return Accepted(new SmsLoginCodeRequestedResponse(
            smsOptions.Value.CodeLifetimeSeconds,
            smsOptions.Value.ResendCooldownSeconds));
    }

    [HttpPost("me/phone/confirmation")]
    [EnableRateLimiting(IdentityServiceConstants.RateLimits.Authentication)]
    public async Task<IActionResult> ConfirmPhoneNumber(
        ConfirmPhoneNumberRequest request,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(user.PhoneNumber) || user.PhoneNumberConfirmed)
            return BadRequest(new ProblemDetails { Title = "当前账户没有待验证的手机号。" });

        var userId = await smsVerification.ConsumePhoneConfirmationCodeAsync(
            user.PhoneNumber,
            request.Code,
            cancellationToken);
        if (!string.Equals(userId, user.Id, StringComparison.Ordinal))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "验证失败",
                detail: "验证码无效或已过期。");
        }

        user.PhoneNumberConfirmed = true;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [Authorize(Policy = "auth.admin.read")]
    [HttpGet("admin")]
    public async Task<IActionResult> List(
        string? search = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var normalizedTerm = term.ToUpperInvariant();
            query = query.Where(user =>
                user.Id == term ||
                user.NormalizedEmail != null && user.NormalizedEmail.Contains(normalizedTerm) ||
                user.DisplayName.ToUpper().Contains(normalizedTerm) ||
                user.PhoneNumber != null && user.PhoneNumber.Contains(term));
        }

        return Ok(await query
            .OrderBy(user => user.Id)
            .Skip(Math.Max(0, skip))
            .Take(take)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.CreatedOnUtc
            })
            .ToListAsync(cancellationToken));
    }

    [Authorize(Policy = "auth.admin.read")]
    [HttpGet("admin/roles")]
    public Task<string[]> ListRoles(CancellationToken cancellationToken) => roles.Roles
        .AsNoTracking()
        .Where(role => role.Name != null)
        .OrderBy(role => role.Name)
        .Select(role => role.Name!)
        .ToArrayAsync(cancellationToken);

    [Authorize(Policy = "auth.admin.read")]
    [HttpGet("admin/{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        return Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.EmailConfirmed,
            user.IsActive,
            user.CreatedOnUtc,
            user.LastLoginOnUtc,
            Roles = await users.GetRolesAsync(user)
        });
    }

    [Authorize(Policy = "auth.admin.write")]
    [HttpPost("admin/{id}/roles/{role}")]
    public async Task<IActionResult> AddRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        if (!await roles.RoleExistsAsync(role))
            return BadRequest(new { error = "Role does not exist." });

        var result = await users.AddToRoleAsync(user, role);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [Authorize(Policy = "auth.admin.write")]
    [HttpDelete("admin/{id}/roles/{role}")]
    public async Task<IActionResult> RemoveRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        if (!await users.IsInRoleAsync(user, role))
            return NoContent();

        var result = await users.RemoveFromRoleAsync(user, role);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [Authorize(Policy = "auth.admin.write")]
    [HttpPost("admin/{id}/activation")]
    public async Task<IActionResult> SetActivation(string id, SetActivationRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        user.IsActive = request.IsActive;
        if (!request.IsActive)
            await users.UpdateSecurityStampAsync(user);
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }
}

/// <summary>更新当前账户的基础资料。</summary>
public sealed record UpdateProfileRequest(
    [Required, StringLength(100, MinimumLength = 1)] string DisplayName,
    [StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string? PhoneNumber);

/// <summary>启用或停用账户。</summary>
public sealed record SetActivationRequest(bool IsActive);

/// <summary>确认当前账户手机号。</summary>
public sealed record ConfirmPhoneNumberRequest(
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^[0-9]{6}$")]
    string Code);
