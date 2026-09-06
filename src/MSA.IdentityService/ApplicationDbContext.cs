using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MSA.IdentityService.Domain;
namespace MSA.IdentityService;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();
    public DbSet<LoyaltyPointAccount> LoyaltyPointAccounts => Set<LoyaltyPointAccount>();
    public DbSet<LoyaltyPointEntry> LoyaltyPointEntries => Set<LoyaltyPointEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsurePointLedgerIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsurePointLedgerIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureApplicationUsers(builder);
        ConfigureUserAddresses(builder);
        ConfigureLoyaltyPoints(builder);
    }

    private static void ConfigureApplicationUsers(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasComment("用户对外展示名称");
            entity.Property(user => user.IsActive).HasComment("账号是否允许登录和执行业务操作");
            entity.Property(user => user.CreatedOnUtc).HasComment("账号创建的 UTC 时间");
            entity.Property(user => user.LastLoginOnUtc).HasComment("最近一次成功登录的 UTC 时间");
        });
    }

    private static void ConfigureUserAddresses(ModelBuilder builder)
    {
        builder.Entity<UserAddress>(entity =>
        {
            entity.ToTable("user_addresses", table =>
            {
                table.HasCheckConstraint(
                    "CK_user_addresses_country_code",
                    "\"CountryCode\" ~ '^[A-Z]{2}$'");
                table.HasCheckConstraint(
                    "CK_user_addresses_active_default",
                    "NOT \"IsDefault\" OR (\"IsEnabled\" AND \"DeletedOnUtc\" IS NULL)");
                table.HasCheckConstraint(
                    "CK_user_addresses_deletion",
                    "\"DeletedOnUtc\" IS NULL OR NOT \"IsEnabled\"");
                table.HasCheckConstraint("CK_user_addresses_version", "\"Version\" >= 0");
            });
            entity.HasKey(address => address.Id);
            entity.HasIndex(address => new { address.UserId, address.CreatedOnUtc });
            entity.HasIndex(address => address.UserId)
                .IsUnique()
                .HasFilter("\"IsDefault\" AND \"IsEnabled\" AND \"DeletedOnUtc\" IS NULL");
            entity.Property(address => address.UserId).HasMaxLength(450).HasComment("地址所属用户标识");
            entity.Property(address => address.Label).HasMaxLength(50).HasComment("用户自定义地址标签");
            entity.Property(address => address.RecipientName).HasMaxLength(100);
            entity.Property(address => address.PhoneNumber).HasMaxLength(32);
            entity.Property(address => address.CountryCode).HasMaxLength(2).IsFixedLength().HasComment("ISO 3166-1 alpha-2 国家或地区代码");
            entity.Property(address => address.Province).HasMaxLength(100);
            entity.Property(address => address.City).HasMaxLength(100);
            entity.Property(address => address.District).HasMaxLength(100);
            entity.Property(address => address.Street).HasMaxLength(300);
            entity.Property(address => address.PostalCode).HasMaxLength(20);
            entity.Property(address => address.IsDefault).HasComment("是否为用户当前默认有效地址");
            entity.Property(address => address.IsEnabled).HasComment("地址是否可用于新订单");
            entity.Property(address => address.DeletedOnUtc).HasComment("地址软删除的 UTC 时间");
            entity.Property(address => address.Version).IsConcurrencyToken().HasComment("地址乐观并发版本号");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(address => address.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureLoyaltyPoints(ModelBuilder builder)
    {
        builder.Entity<LoyaltyPointAccount>(entity =>
        {
            entity.ToTable("loyalty_point_accounts", table =>
            {
                table.HasCheckConstraint(
                    "CK_loyalty_point_accounts_balances",
                    "\"Balance\" >= 0 AND \"LifetimeEarned\" >= 0 AND \"LifetimeSpent\" >= 0 AND \"Balance\" = \"LifetimeEarned\" - \"LifetimeSpent\"");
                table.HasCheckConstraint("CK_loyalty_point_accounts_version", "\"Version\" >= 0");
            });
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id).HasColumnName("UserId").HasMaxLength(450).HasComment("积分账户所属用户标识");
            entity.Property(account => account.Balance).HasComment("当前可用积分余额");
            entity.Property(account => account.LifetimeEarned).HasComment("账户历史累计获得积分");
            entity.Property(account => account.LifetimeSpent).HasComment("账户历史累计消费积分");
            entity.Property(account => account.Version).IsConcurrencyToken().HasComment("积分账户乐观并发版本号");
            entity.HasOne<ApplicationUser>()
                .WithOne()
                .HasForeignKey<LoyaltyPointAccount>(account => account.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LoyaltyPointEntry>(entity =>
        {
            entity.ToTable("loyalty_point_entries", table =>
            {
                table.HasCheckConstraint("CK_loyalty_point_entries_delta", "\"Delta\" <> 0");
                table.HasCheckConstraint(
                    "CK_loyalty_point_entries_balances",
                    "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0 AND \"BalanceAfter\" = \"BalanceBefore\" + \"Delta\"");
            });
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => new { entry.UserId, entry.BusinessKey }).IsUnique();
            entity.HasIndex(entry => new { entry.UserId, entry.OccurredOnUtc });
            entity.Property(entry => entry.UserId).HasMaxLength(450).HasComment("积分流水所属用户标识");
            entity.Property(entry => entry.BusinessKey).HasMaxLength(128).HasComment("积分业务幂等键");
            entity.Property(entry => entry.Delta).HasComment("本次积分变动量，正数为增加、负数为扣减");
            entity.Property(entry => entry.BalanceBefore).HasComment("积分变动前余额");
            entity.Property(entry => entry.BalanceAfter).HasComment("积分变动后余额");
            entity.Property(entry => entry.Source).HasMaxLength(64).HasComment("积分变动来源类型");
            entity.Property(entry => entry.Reason).HasMaxLength(500).HasComment("积分变动原因");
            entity.Property(entry => entry.OperatorId).HasMaxLength(200).HasComment("发起积分变动的主体标识");
            entity.Property(entry => entry.OccurredOnUtc).HasComment("积分变动发生的 UTC 时间");
            entity.HasOne<LoyaltyPointAccount>()
                .WithMany()
                .HasForeignKey(entry => entry.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void EnsurePointLedgerIsAppendOnly()
    {
        // 积分余额可以修正，但历史流水只允许追加，确保审计链和余额推导不被改写。
        if (ChangeTracker.Entries<LoyaltyPointEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Loyalty point ledger entries are immutable.");
        }
    }
}
