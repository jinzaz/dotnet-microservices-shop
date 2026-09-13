namespace MSA.BFF.BM.Authorization;

public enum PbacAction
{
    Query = 1,
    Create = 2,
    Edit = 3,
    Delete = 4
}

public enum PbacEffect
{
    Allow = 1,
    Deny = 2
}

internal sealed class BmUser
{
    private BmUser()
    {
    }

    internal BmUser(string identityUserId, bool isActive, DateTimeOffset createdOnUtc)
    {
        IdentityUserId = identityUserId.Trim();
        IsActive = isActive;
        CreatedOnUtc = createdOnUtc;
    }

    public string IdentityUserId { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public ICollection<BmUserRole> Roles { get; } = [];
    public ICollection<PbacUserPolicy> Policies { get; } = [];

    internal void SetActive(bool isActive) => IsActive = isActive;
}

internal sealed class BmRole
{
    private BmRole()
    {
    }

    internal BmRole(
        Guid id,
        string code,
        string displayName,
        bool isActive,
        DateTimeOffset createdOnUtc)
    {
        Id = id;
        SetNames(code, displayName);
        IsActive = isActive;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public ICollection<BmUserRole> Users { get; } = [];
    public ICollection<PbacRolePolicy> Policies { get; } = [];

    internal void Update(string code, string displayName, bool isActive)
    {
        SetNames(code, displayName);
        IsActive = isActive;
    }

    private void SetNames(string code, string displayName)
    {
        Code = code.Trim();
        NormalizedCode = Code.ToUpperInvariant();
        DisplayName = displayName.Trim();
    }
}

internal sealed class BmUserRole
{
    private BmUserRole()
    {
    }

    internal BmUserRole(string userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public string UserId { get; private set; } = string.Empty;
    public Guid RoleId { get; private set; }
    public BmUser User { get; private set; } = null!;
    public BmRole Role { get; private set; } = null!;
}

internal sealed class PbacPermission
{
    private PbacPermission()
    {
    }

    internal PbacPermission(
        Guid id,
        string code,
        string module,
        PbacAction action,
        string displayName)
    {
        Id = id;
        Code = code;
        Module = module;
        Action = action;
        DisplayName = displayName;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public PbacAction Action { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public ICollection<PbacRoutePermission> Routes { get; } = [];
    public ICollection<PbacPolicyPermission> Policies { get; } = [];
}

internal sealed class PbacRoutePermission
{
    private PbacRoutePermission()
    {
    }

    internal PbacRoutePermission(
        Guid id,
        Guid permissionId,
        string httpMethod,
        string pathTemplate,
        int order)
    {
        Id = id;
        PermissionId = permissionId;
        HttpMethod = httpMethod;
        PathTemplate = pathTemplate;
        Order = order;
    }

    public Guid Id { get; private set; }
    public Guid PermissionId { get; private set; }
    public string HttpMethod { get; private set; } = string.Empty;
    public string PathTemplate { get; private set; } = string.Empty;
    public int Order { get; private set; }
    public PbacPermission Permission { get; private set; } = null!;
}

internal sealed class PbacPolicy
{
    private PbacPolicy()
    {
    }

    internal PbacPolicy(Guid id, string name, PbacEffect effect, DateTimeOffset createdOnUtc)
    {
        Id = id;
        SetName(name);
        Effect = effect;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public PbacEffect Effect { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public ICollection<PbacPolicyPermission> Permissions { get; } = [];
    public ICollection<PbacUserPolicy> Users { get; } = [];
    public ICollection<PbacRolePolicy> Roles { get; } = [];

    internal void Update(string name, PbacEffect effect)
    {
        SetName(name);
        Effect = effect;
    }

    internal void AddAssignments(
        IEnumerable<Guid> permissionIds,
        IEnumerable<string> userIds,
        IEnumerable<Guid> roleIds)
    {
        foreach (var permissionId in permissionIds.Distinct())
            Permissions.Add(new PbacPolicyPermission(Id, permissionId));
        foreach (var userId in userIds.Select(value => value.Trim()).Distinct(StringComparer.Ordinal))
            Users.Add(new PbacUserPolicy(Id, userId));
        foreach (var roleId in roleIds.Distinct())
            Roles.Add(new PbacRolePolicy(Id, roleId));
    }

    private void SetName(string name)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
    }
}

internal sealed class PbacPolicyPermission
{
    private PbacPolicyPermission()
    {
    }

    internal PbacPolicyPermission(Guid policyId, Guid permissionId)
    {
        PolicyId = policyId;
        PermissionId = permissionId;
    }

    public Guid PolicyId { get; private set; }
    public Guid PermissionId { get; private set; }
    public PbacPolicy Policy { get; private set; } = null!;
    public PbacPermission Permission { get; private set; } = null!;
}

internal sealed class PbacUserPolicy
{
    private PbacUserPolicy()
    {
    }

    internal PbacUserPolicy(Guid policyId, string userId)
    {
        PolicyId = policyId;
        UserId = userId.Trim();
    }

    public Guid PolicyId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public PbacPolicy Policy { get; private set; } = null!;
    public BmUser User { get; private set; } = null!;
}

internal sealed class PbacRolePolicy
{
    private PbacRolePolicy()
    {
    }

    internal PbacRolePolicy(Guid policyId, Guid roleId)
    {
        PolicyId = policyId;
        RoleId = roleId;
    }

    public Guid PolicyId { get; private set; }
    public Guid RoleId { get; private set; }
    public PbacPolicy Policy { get; private set; } = null!;
    public BmRole Role { get; private set; } = null!;
}
