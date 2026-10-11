using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace MSA.BFF.BM.Authorization;

internal sealed class PbacOptions
{
    internal const string SectionName = "Pbac";
    public string[] BootstrapAdminUserIds { get; init; } = [];
}

internal static class PbacDefaults
{
    internal const string ProxyPolicy = "bm-pbac-proxy";

    internal static string ManagementPolicy(PbacAction action) =>
        $"bm-pbac-management-{action.ToString().ToLowerInvariant()}";

    internal static string ManagementPermission(PbacAction action) =>
        $"system.authorization.{action.ToString().ToLowerInvariant()}";

    internal static string PermissionPolicy(string permissionCode) =>
        $"bm-pbac-permission:{permissionCode}";
}

internal sealed class PbacProxyRequirement : IAuthorizationRequirement;

internal sealed record PbacPermissionRequirement(
    string PermissionCode,
    bool AllowBootstrapAdministrator = false)
    : IAuthorizationRequirement;

internal sealed class PbacProxyAuthorizationHandler(PbacAuthorizer authorizer)
    : AuthorizationHandler<PbacProxyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PbacProxyRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            await authorizer.IsAllowedAsync(
                context.User,
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class PbacPermissionAuthorizationHandler(
    PbacAuthorizer authorizer,
    IOptions<PbacOptions> options)
    : AuthorizationHandler<PbacPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PbacPermissionRequirement requirement)
    {
        var subject = PbacAuthorizer.GetSubject(context.User);
        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted
            ?? CancellationToken.None;
        if (subject is not null &&
            ((requirement.AllowBootstrapAdministrator &&
              options.Value.BootstrapAdminUserIds.Contains(subject, StringComparer.Ordinal)) ||
             await authorizer.IsAllowedAsync(context.User, requirement.PermissionCode, cancellationToken)))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class PbacAuthorizer(BffBmDbContext db, IOptions<PbacOptions> options)
{
    internal async Task<bool> IsAllowedAsync(
        ClaimsPrincipal principal,
        string httpMethod,
        PathString path,
        CancellationToken cancellationToken)
    {
        var method = HttpMethods.IsHead(httpMethod) ? HttpMethods.Get : httpMethod.ToUpperInvariant();
        var routes = await db.PbacRoutePermissions
            .AsNoTracking()
            .Where(route => route.HttpMethod == method)
            .OrderBy(route => route.Order)
            .Select(route => new PbacRouteRule(
                route.Permission.Code,
                route.PathTemplate))
            .ToListAsync(cancellationToken);
        var permissionCode = PbacRouteMatcher.FindPermissionCode(routes, path);
        return permissionCode is not null &&
               await IsAllowedAsync(principal, permissionCode, cancellationToken);
    }

    internal async Task<bool> IsAllowedAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        var subject = await ResolveSubjectAsync(principal, cancellationToken);
        if (subject is null)
            return false;

        var effects = await db.PbacPolicies
            .AsNoTracking()
            .Where(policy =>
                policy.Permissions.Any(link => link.Permission.Code == permissionCode) &&
                (policy.Users.Any(link => link.UserId == subject.UserId) ||
                 policy.Roles.Any(link => subject.RoleIds.Contains(link.RoleId))))
            .Select(policy => policy.Effect)
            .ToListAsync(cancellationToken);

        return effects.Contains(PbacEffect.Allow) && !effects.Contains(PbacEffect.Deny);
    }

    internal async Task<IReadOnlyList<string>> ListEffectivePermissionCodesAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        // 引导管理员在服务端本来就能管理权限（管理策略对他放行），这里也要把这几项权限告诉前端，
        // 否则首次部署时 BM 里还没有任何成员，他看不到"权限"页面，没法给自己建账号、分配角色。
        string[] bootstrap = GetSubject(principal) is { } identityUserId &&
                             options.Value.BootstrapAdminUserIds.Contains(identityUserId, StringComparer.Ordinal)
            ? Enum.GetValues<PbacAction>().Select(PbacDefaults.ManagementPermission).ToArray()
            : [];

        var subject = await ResolveSubjectAsync(principal, cancellationToken);
        if (subject is null)
            return bootstrap.Order(StringComparer.Ordinal).ToArray();

        var grants = await db.PbacPolicyPermissions
            .AsNoTracking()
            .Where(link =>
                link.Policy.Users.Any(user => user.UserId == subject.UserId) ||
                link.Policy.Roles.Any(role => subject.RoleIds.Contains(role.RoleId)))
            .Select(link => new { link.Permission.Code, link.Policy.Effect })
            .ToListAsync(cancellationToken);

        return grants
            .GroupBy(grant => grant.Code, StringComparer.Ordinal)
            .Where(group => group.Any(grant => grant.Effect == PbacEffect.Allow) &&
                            group.All(grant => grant.Effect != PbacEffect.Deny))
            .Select(group => group.Key)
            .Union(bootstrap, StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? GetSubject(ClaimsPrincipal principal) =>
        principal.FindFirstValue(OpenIddictConstants.Claims.Subject)
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<BmSubject?> ResolveSubjectAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var subject = GetSubject(principal);
        if (subject is null)
            return null;

        var user = await db.BmUsers
            .AsNoTracking()
            .Include(value => value.Roles)
            .ThenInclude(value => value.Role)
            .SingleOrDefaultAsync(
                value => value.IdentityUserId == subject && value.IsActive,
                cancellationToken);
        if (user is null)
            return null;

        return new BmSubject(
            user.IdentityUserId,
            user.Roles
                .Where(value => value.Role.IsActive)
                .Select(value => value.RoleId)
                .ToArray());
    }
}

internal sealed record BmSubject(string UserId, IReadOnlyList<Guid> RoleIds);

internal sealed record PbacRouteRule(string PermissionCode, string PathTemplate);

internal static class PbacRouteMatcher
{
    internal static string? FindPermissionCode(
        IEnumerable<PbacRouteRule> routes,
        PathString path)
    {
        foreach (var route in routes)
        {
            var template = TemplateParser.Parse(route.PathTemplate.TrimStart('/'));
            var matcher = new TemplateMatcher(template, new RouteValueDictionary());
            if (matcher.TryMatch(path, new RouteValueDictionary()))
                return route.PermissionCode;
        }

        return null;
    }
}
