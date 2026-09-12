using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Application.Sms;

namespace MSA.IdentityService.Controllers;

[Authorize]
[ApiController]
[Route("Auth/users")]
public sealed class UsersController(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles) : ControllerBase
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

    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> List(
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        return Ok(await users.Users
            .AsNoTracking()
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

    [Authorize(Roles = "admin")]
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

    [Authorize(Roles = "admin")]
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
