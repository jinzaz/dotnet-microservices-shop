using Microsoft.EntityFrameworkCore;

namespace MSA.BFF.BM.Authorization;

internal sealed class BmDirectoryStore(BffBmDbContext db, TimeProvider timeProvider)
{
    internal async Task<IReadOnlyList<BmUserView>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await db.BmUsers
            .AsNoTracking()
            .Include(user => user.Roles)
            .ThenInclude(link => link.Role)
            .OrderBy(user => user.IdentityUserId)
            .ToListAsync(cancellationToken);
        return users.Select(ToView).ToArray();
    }

    internal async Task<BmUserView?> FindUserAsync(
        string identityUserId,
        CancellationToken cancellationToken)
    {
        var user = await db.BmUsers
            .AsNoTracking()
            .Include(value => value.Roles)
            .ThenInclude(link => link.Role)
            .SingleOrDefaultAsync(
                value => value.IdentityUserId == identityUserId,
                cancellationToken);
        return user is null ? null : ToView(user);
    }

    internal async Task<PbacWriteResult<BmUserView>> CreateUserAsync(
        string identityUserId,
        bool isActive,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var userId = identityUserId.Trim();
        if (await db.BmUsers.AnyAsync(user => user.IdentityUserId == userId, cancellationToken))
            return PbacWriteResult<BmUserView>.Conflict();
        if (!await RolesExistAsync(roleIds, cancellationToken))
            return PbacWriteResult<BmUserView>.NotFound();

        var user = new BmUser(userId, isActive, timeProvider.GetUtcNow());
        foreach (var roleId in roleIds.Distinct())
            user.Roles.Add(new BmUserRole(userId, roleId));
        db.BmUsers.Add(user);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<BmUserView>.Conflict();

        return PbacWriteResult<BmUserView>.Success(
            (await FindUserAsync(userId, cancellationToken))!);
    }

    internal async Task<PbacWriteResult<BmUserView>> UpdateUserAsync(
        string identityUserId,
        bool isActive,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (!await RolesExistAsync(roleIds, cancellationToken))
            return PbacWriteResult<BmUserView>.NotFound();

        var user = await db.BmUsers
            .Include(value => value.Roles)
            .SingleOrDefaultAsync(
                value => value.IdentityUserId == identityUserId,
                cancellationToken);
        if (user is null)
            return PbacWriteResult<BmUserView>.NotFound();

        user.SetActive(isActive);
        SyncUserRoles(user, roleIds);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<BmUserView>.Conflict();

        return PbacWriteResult<BmUserView>.Success(
            (await FindUserAsync(identityUserId, cancellationToken))!);
    }

    internal async Task<bool> DeleteUserAsync(
        string identityUserId,
        CancellationToken cancellationToken)
    {
        var user = await db.BmUsers.FindAsync([identityUserId], cancellationToken);
        if (user is null)
            return false;
        db.BmUsers.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal async Task<IReadOnlyList<BmRoleView>> ListRolesAsync(CancellationToken cancellationToken) =>
        await db.BmRoles
            .AsNoTracking()
            .OrderBy(role => role.Code)
            .Select(role => new BmRoleView(
                role.Id,
                role.Code,
                role.DisplayName,
                role.IsActive,
                role.CreatedOnUtc))
            .ToListAsync(cancellationToken);

    internal Task<BmRoleView?> FindRoleAsync(Guid id, CancellationToken cancellationToken) =>
        db.BmRoles
            .AsNoTracking()
            .Where(role => role.Id == id)
            .Select(role => new BmRoleView(
                role.Id,
                role.Code,
                role.DisplayName,
                role.IsActive,
                role.CreatedOnUtc))
            .SingleOrDefaultAsync(cancellationToken);

    internal async Task<PbacWriteResult<BmRoleView>> CreateRoleAsync(
        string code,
        string displayName,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var role = new BmRole(Guid.NewGuid(), code, displayName, isActive, timeProvider.GetUtcNow());
        if (await db.BmRoles.AnyAsync(
                value => value.NormalizedCode == role.NormalizedCode,
                cancellationToken))
        {
            return PbacWriteResult<BmRoleView>.Conflict();
        }

        db.BmRoles.Add(role);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<BmRoleView>.Conflict();
        return PbacWriteResult<BmRoleView>.Success(ToView(role));
    }

    internal async Task<PbacWriteResult<BmRoleView>> UpdateRoleAsync(
        Guid id,
        string code,
        string displayName,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (id == PbacSeedData.SystemAdministratorRoleId)
            return PbacWriteResult<BmRoleView>.Conflict();

        var role = await db.BmRoles.FindAsync([id], cancellationToken);
        if (role is null)
            return PbacWriteResult<BmRoleView>.NotFound();
        role.Update(code, displayName, isActive);
        if (!await TrySaveAsync(cancellationToken))
            return PbacWriteResult<BmRoleView>.Conflict();
        return PbacWriteResult<BmRoleView>.Success(ToView(role));
    }

    internal async Task<PbacWriteStatus> DeleteRoleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == PbacSeedData.SystemAdministratorRoleId)
            return PbacWriteStatus.Conflict;
        var role = await db.BmRoles.FindAsync([id], cancellationToken);
        if (role is null)
            return PbacWriteStatus.NotFound;
        db.BmRoles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return PbacWriteStatus.Success;
    }

    private async Task<bool> RolesExistAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToArray();
        if (ids.Length == 0)
            return true;
        return await db.BmRoles.CountAsync(role => ids.Contains(role.Id), cancellationToken) == ids.Length;
    }

    private void SyncUserRoles(BmUser user, IReadOnlyCollection<Guid> roleIds)
    {
        var desired = roleIds.ToHashSet();
        foreach (var link in user.Roles.Where(link => !desired.Contains(link.RoleId)).ToArray())
            db.BmUserRoles.Remove(link);
        var current = user.Roles.Select(link => link.RoleId).ToHashSet();
        foreach (var roleId in desired.Where(roleId => !current.Contains(roleId)))
            user.Roles.Add(new BmUserRole(user.IdentityUserId, roleId));
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

    private static BmUserView ToView(BmUser user) => new(
        user.IdentityUserId,
        user.IsActive,
        user.CreatedOnUtc,
        user.Roles
            .Select(link => ToView(link.Role))
            .OrderBy(role => role.Code, StringComparer.Ordinal)
            .ToArray());

    private static BmRoleView ToView(BmRole role) => new(
        role.Id,
        role.Code,
        role.DisplayName,
        role.IsActive,
        role.CreatedOnUtc);
}

internal sealed record BmUserView(
    string IdentityUserId,
    bool IsActive,
    DateTimeOffset CreatedOnUtc,
    IReadOnlyList<BmRoleView> Roles);

internal sealed record BmRoleView(
    Guid Id,
    string Code,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedOnUtc);

internal enum PbacWriteStatus
{
    Success,
    NotFound,
    Conflict
}

internal sealed record PbacWriteResult<T>(PbacWriteStatus Status, T? Value)
{
    internal static PbacWriteResult<T> Success(T value) => new(PbacWriteStatus.Success, value);
    internal static PbacWriteResult<T> NotFound() => new(PbacWriteStatus.NotFound, default);
    internal static PbacWriteResult<T> Conflict() => new(PbacWriteStatus.Conflict, default);
}
