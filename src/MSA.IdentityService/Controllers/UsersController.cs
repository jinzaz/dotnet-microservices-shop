using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace MSA.IdentityService.Controllers;

[Authorize]
[ApiController]
[Route("api/users")]
public sealed class UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles) : ControllerBase
{
    [HttpGet("me")] public async Task<IActionResult> Me() { var user = await users.GetUserAsync(User); if (user is null) return Unauthorized(); return Ok(new { user.Id, user.Email, user.DisplayName, user.PhoneNumber, user.IsActive, user.CreatedOnUtc, Roles = await users.GetRolesAsync(user) }); }
    [HttpPut("me")] public async Task<IActionResult> UpdateMe(UpdateProfileRequest r) { var user = await users.GetUserAsync(User); if (user is null) return Unauthorized(); user.DisplayName = r.DisplayName; user.PhoneNumber = r.PhoneNumber; var result = await users.UpdateAsync(user); return result.Succeeded ? NoContent() : BadRequest(result.Errors); }
    [Authorize(Roles = "admin")][HttpGet] public async Task<IActionResult> List(int skip = 0, int take = 50, CancellationToken ct = default) { take = Math.Clamp(take, 1, 100); return Ok(await users.Users.AsNoTracking().OrderBy(x => x.Id).Skip(Math.Max(0, skip)).Take(take).Select(x => new { x.Id, x.Email, x.DisplayName, x.IsActive, x.CreatedOnUtc }).ToListAsync(ct)); }
    [Authorize(Roles = "admin")][HttpPost("{id}/roles/{role}")] public async Task<IActionResult> AddRole(string id, string role) { var user = await users.FindByIdAsync(id); if (user is null) return NotFound(); if (!await roles.RoleExistsAsync(role)) return BadRequest(new { error = "Role does not exist." }); var result = await users.AddToRoleAsync(user, role); return result.Succeeded ? NoContent() : BadRequest(result.Errors); }
    [Authorize(Roles = "admin")][HttpPost("{id}/activation")] public async Task<IActionResult> SetActivation(string id, SetActivationRequest r) { var user = await users.FindByIdAsync(id); if (user is null) return NotFound(); user.IsActive = r.IsActive; if (!r.IsActive) await users.UpdateSecurityStampAsync(user); var result = await users.UpdateAsync(user); return result.Succeeded ? NoContent() : BadRequest(result.Errors); }
}
public sealed record UpdateProfileRequest(string DisplayName, string? PhoneNumber); public sealed record SetActivationRequest(bool IsActive);
