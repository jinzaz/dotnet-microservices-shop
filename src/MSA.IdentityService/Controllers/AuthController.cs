using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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
            var request =
                HttpContext.GetOpenIddictServerRequest();

            if (request == null)
            {
                throw new InvalidOperationException();
            }

            // 当前用户没有登录
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Challenge(
                    IdentityConstants.ApplicationScheme);
            }

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Challenge(
                    IdentityConstants.ApplicationScheme);
            }

            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);

            identity.SetClaim(
                OpenIddictConstants.Claims.Subject,
                user.Id);

            identity.SetClaim(
                OpenIddictConstants.Claims.Name,
                user.UserName);

            var principal = new ClaimsPrincipal(identity);

            principal.SetScopes(request.GetScopes());

            return SignIn(
                principal,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }
}
