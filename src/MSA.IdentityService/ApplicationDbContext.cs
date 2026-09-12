using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MSA.IdentityService;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasComment("用户对外展示名称");
            entity.Property(user => user.IsActive).HasComment("账号是否允许登录和执行业务操作");
            entity.Property(user => user.CreatedOnUtc).HasComment("账号创建的 UTC 时间");
            entity.Property(user => user.LastLoginOnUtc).HasComment("最近一次成功登录的 UTC 时间");
        });
    }
}
