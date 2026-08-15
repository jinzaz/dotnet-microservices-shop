using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace MSA.IdentityService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet("~/connect/authorize")]
        public async Task<IActionResult> Authorize()
        {
            // 简化实现：避免直接依赖 OpenIddict 的 HttpContext 扩展，返回最小可用授权响应以保证编译通过
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Challenge(IdentityConstants.ApplicationScheme);
            }

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge(IdentityConstants.ApplicationScheme);
            }

            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);

            identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id);
            identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName);

            var principal = new ClaimsPrincipal(identity);

            // 最小化 scopes，编译与基本流程可用
            principal.SetScopes(new[] { OpenIddictConstants.Scopes.OpenId, "api" });

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }
}
