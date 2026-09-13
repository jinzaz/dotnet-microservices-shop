using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MSA.BFF.BM.Authorization;

internal sealed class BmUserConfiguration : IEntityTypeConfiguration<BmUser>
{
    public void Configure(EntityTypeBuilder<BmUser> entity)
    {
        entity.ToTable("bm_users");
        entity.HasKey(user => user.IdentityUserId);
        entity.Property(user => user.IdentityUserId)
            .HasColumnName("identity_user_id")
            .HasMaxLength(200)
            .HasComment("Identity 用户 sub；仅逻辑关联，不跨库建外键");
        entity.Property(user => user.IsActive)
            .HasColumnName("is_active")
            .HasComment("后台账号是否允许访问");
        entity.Property(user => user.CreatedOnUtc)
            .HasColumnName("created_on_utc");
    }
}

internal sealed class BmRoleConfiguration : IEntityTypeConfiguration<BmRole>
{
    public void Configure(EntityTypeBuilder<BmRole> entity)
    {
        entity.ToTable("bm_roles");
        entity.HasKey(role => role.Id);
        entity.Property(role => role.Id).HasColumnName("id");
        entity.Property(role => role.Code)
            .HasColumnName("code")
            .HasMaxLength(100);
        entity.Property(role => role.NormalizedCode)
            .HasColumnName("normalized_code")
            .HasMaxLength(100);
        entity.Property(role => role.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(120);
        entity.Property(role => role.IsActive)
            .HasColumnName("is_active")
            .HasComment("停用角色不会参与授权");
        entity.Property(role => role.CreatedOnUtc).HasColumnName("created_on_utc");
        entity.HasIndex(role => role.NormalizedCode)
            .IsUnique()
            .HasDatabaseName("ux_bm_roles_normalized_code");
        entity.HasData(PbacSeedData.SystemAdministratorRole);
    }
}

internal sealed class BmUserRoleConfiguration : IEntityTypeConfiguration<BmUserRole>
{
    public void Configure(EntityTypeBuilder<BmUserRole> entity)
    {
        entity.ToTable("bm_user_roles");
        entity.HasKey(value => new { value.UserId, value.RoleId });
        entity.Property(value => value.UserId)
            .HasColumnName("user_id")
            .HasMaxLength(200);
        entity.Property(value => value.RoleId).HasColumnName("role_id");
        entity.HasOne(value => value.User)
            .WithMany(user => user.Roles)
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(value => value.Role)
            .WithMany(role => role.Users)
            .HasForeignKey(value => value.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PbacPermissionConfiguration : IEntityTypeConfiguration<PbacPermission>
{
    public void Configure(EntityTypeBuilder<PbacPermission> entity)
    {
        entity.ToTable("pbac_permissions", table =>
            table.HasCheckConstraint(
                "ck_pbac_permissions_action",
                "action IN ('Query', 'Create', 'Edit', 'Delete')"));
        entity.HasKey(permission => permission.Id);
        entity.Property(permission => permission.Id).HasColumnName("id");
        entity.Property(permission => permission.Code)
            .HasColumnName("code")
            .HasMaxLength(160)
            .HasComment("模块与操作组成的稳定权限编码");
        entity.Property(permission => permission.Module)
            .HasColumnName("module")
            .HasMaxLength(128)
            .HasComment("后台业务模块编码");
        entity.Property(permission => permission.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasComment("查询、创建、编辑或删除操作");
        entity.Property(permission => permission.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200);
        entity.HasIndex(permission => permission.Code)
            .IsUnique()
            .HasDatabaseName("ux_pbac_permissions_code");
        entity.HasIndex(permission => new { permission.Module, permission.Action })
            .IsUnique()
            .HasDatabaseName("ux_pbac_permissions_module_action");
        entity.HasData(PbacSeedData.Permissions);
    }
}

internal sealed class PbacRoutePermissionConfiguration : IEntityTypeConfiguration<PbacRoutePermission>
{
    public void Configure(EntityTypeBuilder<PbacRoutePermission> entity)
    {
        entity.ToTable("pbac_route_permissions");
        entity.HasKey(route => route.Id);
        entity.Property(route => route.Id).HasColumnName("id");
        entity.Property(route => route.PermissionId)
            .HasColumnName("permission_id")
            .HasComment("该代理路由要求的权限");
        entity.Property(route => route.HttpMethod)
            .HasColumnName("http_method")
            .HasMaxLength(16);
        entity.Property(route => route.PathTemplate)
            .HasColumnName("path_template")
            .HasMaxLength(500)
            .HasComment("以 /api 开头的 BFF 代理路由模板");
        entity.Property(route => route.Order).HasColumnName("match_order");
        entity.HasIndex(route => new { route.HttpMethod, route.PathTemplate })
            .IsUnique()
            .HasDatabaseName("ux_pbac_route_permissions_method_path");
        entity.HasOne(route => route.Permission)
            .WithMany(permission => permission.Routes)
            .HasForeignKey(route => route.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasData(PbacSeedData.Routes);
    }
}

internal sealed class PbacPolicyConfiguration : IEntityTypeConfiguration<PbacPolicy>
{
    public void Configure(EntityTypeBuilder<PbacPolicy> entity)
    {
        entity.ToTable("pbac_policies", table =>
            table.HasCheckConstraint(
                "ck_pbac_policies_effect",
                "effect IN ('Allow', 'Deny')"));
        entity.HasKey(policy => policy.Id);
        entity.Property(policy => policy.Id).HasColumnName("id");
        entity.Property(policy => policy.Name)
            .HasColumnName("name")
            .HasMaxLength(120);
        entity.Property(policy => policy.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(120);
        entity.Property(policy => policy.Effect)
            .HasColumnName("effect")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasComment("允许或拒绝，拒绝优先");
        entity.Property(policy => policy.CreatedOnUtc).HasColumnName("created_on_utc");
        entity.HasIndex(policy => policy.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_pbac_policies_normalized_name");
        entity.HasData(PbacSeedData.SystemAdministratorPolicy);
    }
}

internal sealed class PbacPolicyPermissionConfiguration : IEntityTypeConfiguration<PbacPolicyPermission>
{
    public void Configure(EntityTypeBuilder<PbacPolicyPermission> entity)
    {
        entity.ToTable("pbac_policy_permissions");
        entity.HasKey(value => new { value.PolicyId, value.PermissionId });
        entity.Property(value => value.PolicyId).HasColumnName("policy_id");
        entity.Property(value => value.PermissionId).HasColumnName("permission_id");
        entity.HasOne(value => value.Policy)
            .WithMany(policy => policy.Permissions)
            .HasForeignKey(value => value.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(value => value.Permission)
            .WithMany(permission => permission.Policies)
            .HasForeignKey(value => value.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasData(PbacSeedData.SystemAdministratorPermissions);
    }
}

internal sealed class PbacUserPolicyConfiguration : IEntityTypeConfiguration<PbacUserPolicy>
{
    public void Configure(EntityTypeBuilder<PbacUserPolicy> entity)
    {
        entity.ToTable("pbac_user_policies");
        entity.HasKey(value => new { value.PolicyId, value.UserId });
        entity.Property(value => value.PolicyId).HasColumnName("policy_id");
        entity.Property(value => value.UserId)
            .HasColumnName("user_id")
            .HasMaxLength(200);
        entity.HasOne(value => value.Policy)
            .WithMany(policy => policy.Users)
            .HasForeignKey(value => value.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(value => value.User)
            .WithMany(user => user.Policies)
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PbacRolePolicyConfiguration : IEntityTypeConfiguration<PbacRolePolicy>
{
    public void Configure(EntityTypeBuilder<PbacRolePolicy> entity)
    {
        entity.ToTable("pbac_role_policies");
        entity.HasKey(value => new { value.PolicyId, value.RoleId });
        entity.Property(value => value.PolicyId).HasColumnName("policy_id");
        entity.Property(value => value.RoleId).HasColumnName("role_id");
        entity.HasOne(value => value.Policy)
            .WithMany(policy => policy.Roles)
            .HasForeignKey(value => value.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(value => value.Role)
            .WithMany(role => role.Policies)
            .HasForeignKey(value => value.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasData(PbacSeedData.SystemAdministratorRolePolicy);
    }
}
