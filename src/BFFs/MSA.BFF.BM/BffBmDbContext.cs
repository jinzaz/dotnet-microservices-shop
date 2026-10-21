using Microsoft.EntityFrameworkCore;
using MSA.BFF.BM.Authorization;

namespace MSA.BFF.BM;

public sealed class BffBmDbContext(DbContextOptions<BffBmDbContext> options) : DbContext(options)
{
    internal DbSet<BmUser> BmUsers => Set<BmUser>();
    internal DbSet<BmRole> BmRoles => Set<BmRole>();
    internal DbSet<BmUserRole> BmUserRoles => Set<BmUserRole>();
    internal DbSet<PbacPermission> PbacPermissions => Set<PbacPermission>();
    internal DbSet<PbacRoutePermission> PbacRoutePermissions => Set<PbacRoutePermission>();
    internal DbSet<PbacPolicy> PbacPolicies => Set<PbacPolicy>();
    internal DbSet<PbacPolicyPermission> PbacPolicyPermissions => Set<PbacPolicyPermission>();
    internal DbSet<PbacUserPolicy> PbacUserPolicies => Set<PbacUserPolicy>();
    internal DbSet<PbacRolePolicy> PbacRolePolicies => Set<PbacRolePolicy>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseEnumCheckConstraints();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bff_bm");
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BffBmDbContext).Assembly);
    }
}
