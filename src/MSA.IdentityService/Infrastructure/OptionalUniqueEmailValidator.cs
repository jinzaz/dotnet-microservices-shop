using Microsoft.AspNetCore.Identity;

namespace MSA.IdentityService.Infrastructure;

/// <summary>邮箱可以为空（短信登录即注册的账户），填了就必须唯一。错误码沿用 DuplicateEmail，注册接口靠它判断"邮箱已注册"。</summary>
internal sealed class OptionalUniqueEmailValidator : IUserValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
            return IdentityResult.Success;

        var owner = await manager.FindByEmailAsync(email);
        return owner is null || string.Equals(owner.Id, user.Id, StringComparison.Ordinal)
            ? IdentityResult.Success
            : IdentityResult.Failed(manager.ErrorDescriber.DuplicateEmail(email));
    }
}
