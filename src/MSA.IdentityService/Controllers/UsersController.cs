using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MSA.IdentityService.Infrastructure.Throttling;
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
    ApplicationDbContext db,
    ISmsVerificationService smsVerification,
    IOptions<SmsOptions> smsOptions,
    ILogger<UsersController> logger) : ControllerBase
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
        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : request.PhoneNumber.Trim();
        user.DisplayName = SafeDisplayNameAttribute.Normalize(request.DisplayName);
        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = false;
        }

        var result = await users.UpdateAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [HttpPost("me/phone/confirmation/code")]
    [Throttle(ThrottlePolicy.SmsDispatch)]
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
        // 号码已绑定其他账户也照常发码：验证通过即换绑，见 ConfirmPhoneNumber。

        await smsVerification.RequestPhoneConfirmationCodeAsync(
            user.Id,
            user.PhoneNumber,
            cancellationToken);
        return Accepted(new SmsLoginCodeRequestedResponse(
            smsOptions.Value.CodeLifetimeSeconds,
            smsOptions.Value.ResendCooldownSeconds));
    }

    [HttpPost("me/phone/confirmation")]
    [Throttle(ThrottlePolicy.Authentication)]
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previousOwner = await users.Users.FirstOrDefaultAsync(
            candidate => candidate.Id != user.Id
                && candidate.PhoneNumber == user.PhoneNumber
                && candidate.PhoneNumberConfirmed,
            cancellationToken);
        if (previousOwner is not null)
        {
            var released = await ReleasePhoneNumberAsync(previousOwner);
            if (!released.Succeeded)
                return BadRequest(released.Errors);
        }

        user.PhoneNumberConfirmed = true;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await transaction.CommitAsync(cancellationToken);
        if (previousOwner is not null)
        {
            logger.LogInformation(
                "Phone number moved from user {PreviousUserId} to {UserId}; previous account active: {IsActive}.",
                previousOwner.Id,
                user.Id,
                previousOwner.IsActive);
        }
        return NoContent();
    }

    /// <summary>
    /// 换绑：短信验证已证明当前用户持有这个号码，把它从原账户移走（先清原账户，已验证号码有唯一索引）。
    /// 原账户没有邮箱也没有密码时只能靠这个号码短信登录，移走后就是进不去的空壳，按后台停用的方式一并停用。
    /// 原账户的订单、地址等业务数据不迁移，仍挂在原账户 Id 上。
    /// </summary>
    private async Task<IdentityResult> ReleasePhoneNumberAsync(ApplicationUser previousOwner)
    {
        previousOwner.PhoneNumber = null;
        previousOwner.PhoneNumberConfirmed = false;
        if (string.IsNullOrEmpty(previousOwner.Email) && !await users.HasPasswordAsync(previousOwner))
        {
            previousOwner.IsActive = false;
            await users.UpdateSecurityStampAsync(previousOwner);
        }
        return await users.UpdateAsync(previousOwner);
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
    [Required, StringLength(100, MinimumLength = 1), SafeDisplayName] string DisplayName,
    [StringLength(16), RegularExpression(PhoneNumberFormat.E164Pattern, ErrorMessage = PhoneNumberFormat.E164Error)]
    string? PhoneNumber);

/// <summary>启用或停用账户。</summary>
public sealed record SetActivationRequest(bool IsActive);

/// <summary>确认当前账户手机号。</summary>
public sealed record ConfirmPhoneNumberRequest(
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"^[0-9]{6}$")]
    string Code);
