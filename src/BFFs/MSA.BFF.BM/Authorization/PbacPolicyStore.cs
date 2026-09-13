using Microsoft.EntityFrameworkCore;

namespace MSA.BFF.BM.Authorization;

internal sealed class PbacPolicyStore(BffBmDbContext db, TimeProvider timeProvider)
{
    internal async Task<IReadOnlyList<PbacPermissionView>> ListPermissionsAsync(
        CancellationToken cancellationToken)
    {
        var permissions = await db.PbacPermissions
            .AsNoTracking()
            .Include(permission => permission.Routes)
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Action)
            .ToListAsync(cancellationToken);
        return permissions.Select(ToView).ToArray();
    }

    internal async Task<IReadOnlyList<PbacPolicyView>> ListPoliciesAsync(
        CancellationToken cancellationToken)
    {
        var policies = await PolicyQuery()
            .OrderBy(policy => policy.Name)
            .ToListAsync(cancellationToken);
        return policies.Select(ToView).ToArray();
    }

    internal async Task<PbacPolicyView?> FindPolicyAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var policy = await PolicyQuery()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return policy is null ? null : ToView(policy);
    }

    internal async Task<PbacWriteResult<PbacPolicyView>> CreatePolicyAsync(
        string name,
        PbacEffect effect,
        IReadOnlyCollection<Guid> permissionIds,
        IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.Trim().ToUpperInvariant();
        if (await db.PbacPolicies.AnyAsync(
                policy => policy.NormalizedName == normalizedName,
                cancellationToken))
        {
            return PbacWriteResult<PbacPolicyView>.Conflict();
        }
        if (!await ReferencesExistAsync(permissionIds, userIds, roleIds, cancellationToken))
            return PbacWriteResult<PbacPolicyView>.NotFound();

        var policy = new PbacPolicy(Guid.NewGuid(), name, effect, timeProvider.GetUtcNow());
        policy.AddAssignments(permissionIds, userIds, roleIds);
        db.PbacPolicies.Add(policy);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<PbacPolicyView>.Conflict();

        return PbacWriteResult<PbacPolicyView>.Success(
            (await FindPolicyAsync(policy.Id, cancellationToken))!);
    }

    internal async Task<PbacWriteResult<PbacPolicyView>> UpdatePolicyAsync(
        Guid id,
        string name,
        PbacEffect effect,
        IReadOnlyCollection<Guid> permissionIds,
        IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (id == PbacSeedData.SystemAdministratorPolicyId)
            return PbacWriteResult<PbacPolicyView>.Conflict();
        if (!await ReferencesExistAsync(permissionIds, userIds, roleIds, cancellationToken))
            return PbacWriteResult<PbacPolicyView>.NotFound();

        var policy = await db.PbacPolicies
            .Include(value => value.Permissions)
            .Include(value => value.Users)
            .Include(value => value.Roles)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (policy is null)
            return PbacWriteResult<PbacPolicyView>.NotFound();

        policy.Update(name, effect);
        SyncPermissions(policy, permissionIds);
        SyncUsers(policy, userIds);
        SyncRoles(policy, roleIds);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<PbacPolicyView>.Conflict();

        return PbacWriteResult<PbacPolicyView>.Success(
            (await FindPolicyAsync(id, cancellationToken))!);
    }

    internal async Task<PbacWriteStatus> DeletePolicyAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == PbacSeedData.SystemAdministratorPolicyId)
            return PbacWriteStatus.Conflict;
        var policy = await db.PbacPolicies.FindAsync([id], cancellationToken);
        if (policy is null)
            return PbacWriteStatus.NotFound;
        db.PbacPolicies.Remove(policy);
        await db.SaveChangesAsync(cancellationToken);
        return PbacWriteStatus.Success;
    }

    private IQueryable<PbacPolicy> PolicyQuery() => db.PbacPolicies
        .AsNoTracking()
        .Include(policy => policy.Permissions)
        .ThenInclude(link => link.Permission)
        .Include(policy => policy.Users)
        .Include(policy => policy.Roles)
        .ThenInclude(link => link.Role)
        .AsSplitQuery();

    private async Task<bool> ReferencesExistAsync(
        IReadOnlyCollection<Guid> permissionIds,
        IReadOnlyCollection<string> userIds,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var permissions = permissionIds.Distinct().ToArray();
        var users = userIds.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var roles = roleIds.Distinct().ToArray();
        return permissions.Length > 0 &&
               await db.PbacPermissions.CountAsync(
                   value => permissions.Contains(value.Id),
                   cancellationToken) == permissions.Length &&
               await db.BmUsers.CountAsync(
                   value => users.Contains(value.IdentityUserId),
                   cancellationToken) == users.Length &&
               await db.BmRoles.CountAsync(
                   value => roles.Contains(value.Id),
                   cancellationToken) == roles.Length;
    }

    private void SyncPermissions(PbacPolicy policy, IReadOnlyCollection<Guid> permissionIds)
    {
        var desired = permissionIds.ToHashSet();
        foreach (var link in policy.Permissions.Where(link => !desired.Contains(link.PermissionId)).ToArray())
            db.PbacPolicyPermissions.Remove(link);
        var current = policy.Permissions.Select(link => link.PermissionId).ToHashSet();
        foreach (var id in desired.Where(id => !current.Contains(id)))
            policy.Permissions.Add(new PbacPolicyPermission(policy.Id, id));
    }

    private void SyncUsers(PbacPolicy policy, IReadOnlyCollection<string> userIds)
    {
        var desired = userIds.Select(value => value.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach (var link in policy.Users.Where(link => !desired.Contains(link.UserId)).ToArray())
            db.PbacUserPolicies.Remove(link);
        var current = policy.Users.Select(link => link.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in desired.Where(id => !current.Contains(id)))
            policy.Users.Add(new PbacUserPolicy(policy.Id, id));
    }

    private void SyncRoles(PbacPolicy policy, IReadOnlyCollection<Guid> roleIds)
    {
        var desired = roleIds.ToHashSet();
        foreach (var link in policy.Roles.Where(link => !desired.Contains(link.RoleId)).ToArray())
            db.PbacRolePolicies.Remove(link);
        var current = policy.Roles.Select(link => link.RoleId).ToHashSet();
        foreach (var id in desired.Where(id => !current.Contains(id)))
            policy.Roles.Add(new PbacRolePolicy(policy.Id, id));
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static PbacPermissionView ToView(PbacPermission permission) => new(
        permission.Id,
        permission.Code,
        permission.Module,
        permission.Action,
        permission.DisplayName,
        permission.Routes
            .OrderBy(route => route.Order)
            .Select(route => new PbacRouteView(route.HttpMethod, route.PathTemplate))
            .ToArray());

    private static PbacPolicyView ToView(PbacPolicy policy) => new(
        policy.Id,
        policy.Name,
        policy.Effect,
        policy.CreatedOnUtc,
        policy.Permissions
            .Select(link => new PbacPermissionSummary(
                link.Permission.Id,
                link.Permission.Code,
                link.Permission.DisplayName))
            .OrderBy(permission => permission.Code, StringComparer.Ordinal)
            .ToArray(),
        policy.Users
            .Select(link => link.UserId)
            .OrderBy(userId => userId, StringComparer.Ordinal)
            .ToArray(),
        policy.Roles
            .Select(link => new BmRoleView(
                link.Role.Id,
                link.Role.Code,
                link.Role.DisplayName,
                link.Role.IsActive,
                link.Role.CreatedOnUtc))
            .OrderBy(role => role.Code, StringComparer.Ordinal)
            .ToArray());
}

internal sealed record PbacPermissionView(
    Guid Id,
    string Code,
    string Module,
    PbacAction Action,
    string DisplayName,
    IReadOnlyList<PbacRouteView> Routes);

internal sealed record PbacRouteView(string HttpMethod, string PathTemplate);

internal sealed record PbacPermissionSummary(Guid Id, string Code, string DisplayName);

internal sealed record PbacPolicyView(
    Guid Id,
    string Name,
    PbacEffect Effect,
    DateTimeOffset CreatedOnUtc,
    IReadOnlyList<PbacPermissionSummary> Permissions,
    IReadOnlyList<string> UserIds,
    IReadOnlyList<BmRoleView> Roles);
