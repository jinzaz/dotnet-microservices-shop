using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.BasketService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.BasketService.Infrastructure;

public sealed class BasketDbContext(
    DbContextOptions<BasketDbContext> options,
    IMediator mediator) : MessagingDbContext(options, mediator)
{
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<BasketItem> BasketItems => Set<BasketItem>();
    public DbSet<BasketCoupon> BasketCoupons => Set<BasketCoupon>();
    public DbSet<BasketProductSnapshot> ProductSnapshots => Set<BasketProductSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Basket>(entity =>
        {
            entity.ToTable("baskets", table =>
            {
                table.HasCheckConstraint("CK_baskets_version", "\"Version\" >= 0");
                table.HasCheckConstraint("CK_baskets_user", "char_length(trim(\"UserId\")) > 0");
                table.HasCheckConstraint("CK_baskets_timestamps", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.UserId).IsUnique();
            entity.Property(value => value.UserId).HasMaxLength(200).HasComment("购物车所属用户标识");
            entity.Property(value => value.UpdatedOnUtc).HasComment("购物车最后变更的 UTC 时间");
            entity.Property(value => value.Version).IsConcurrencyToken().HasComment("购物车乐观并发版本号");
            entity.HasMany(value => value.Items)
                .WithOne()
                .HasForeignKey(value => value.BasketId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(value => value.Coupons)
                .WithOne()
                .HasForeignKey(value => value.BasketId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(value => value.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(value => value.Coupons).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<BasketItem>(entity =>
        {
            entity.ToTable("basket_items", table =>
            {
                table.HasCheckConstraint(
                    "CK_basket_items_quantity",
                    $"\"Quantity\" BETWEEN 1 AND {BasketLimits.MaximumSupportedLineQuantity}");
                table.HasCheckConstraint("CK_basket_items_sku", "\"Sku\" = upper(trim(\"Sku\"))");
                table.HasCheckConstraint("CK_basket_items_price", "\"UnitPriceAtSelection\" >= 0");
                table.HasCheckConstraint("CK_basket_items_currency", "\"CurrencyAtSelection\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("CK_basket_items_revision", "\"ProductRevisionAtSelection\" >= 0");
                table.HasCheckConstraint("CK_basket_items_timestamps", "\"UpdatedOnUtc\" >= \"AddedOnUtc\"");
            });
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BasketId, value.Sku }).IsUnique();
            entity.HasIndex(value => new { value.BasketId, value.ProductId }).IsUnique();
            entity.Property(value => value.Sku).HasMaxLength(80).HasComment("加入购物车时的标准化 SKU");
            entity.Property(value => value.ProductNameAtSelection).HasMaxLength(200).HasComment("最近选择或增量更新时的商品名称快照");
            entity.Property(value => value.UnitPriceAtSelection).HasPrecision(18, 2).HasComment("最近选择或增量更新时的商品单价快照");
            entity.Property(value => value.CurrencyAtSelection).HasMaxLength(3).IsFixedLength().HasComment("商品价格快照的三位币种代码");
            entity.Property(value => value.ProductRevisionAtSelection).HasComment("商品快照对应的目录修订号");
            entity.Property(value => value.Quantity).HasComment("当前购买数量");
        });

        modelBuilder.Entity<BasketCoupon>(entity =>
        {
            entity.ToTable("basket_coupons", table =>
                table.HasCheckConstraint("CK_basket_coupons_code", "\"Code\" = upper(trim(\"Code\"))"));
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.BasketId, value.Code }).IsUnique();
            entity.Property(value => value.Code).HasMaxLength(80).HasComment("待在结算时校验的标准化优惠券码");
        });

        modelBuilder.Entity<BasketProductSnapshot>(entity =>
        {
            entity.ToTable("basket_product_snapshots", table =>
            {
                table.HasCheckConstraint("CK_basket_product_snapshots_sku", "\"Sku\" = upper(trim(\"Sku\"))");
                table.HasCheckConstraint("CK_basket_product_snapshots_price", "\"Price\" >= 0");
                table.HasCheckConstraint("CK_basket_product_snapshots_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("CK_basket_product_snapshots_revision", "\"SourceRevision\" >= 0");
            });
            entity.HasKey(value => value.ProductId);
            entity.HasIndex(value => value.Sku).IsUnique();
            entity.HasIndex(value => new { value.IsAvailable, value.Sku });
            entity.Property(value => value.Sku).HasMaxLength(80).HasComment("目录服务发布的标准化 SKU");
            entity.Property(value => value.Name).HasMaxLength(200).HasComment("目录商品名称的本地投影");
            entity.Property(value => value.Price).HasPrecision(18, 2).HasComment("目录商品当前单价的本地投影");
            entity.Property(value => value.Currency).HasMaxLength(3).IsFixedLength().HasComment("目录商品价格的三位币种代码");
            entity.Property(value => value.IsAvailable).HasComment("目录商品当前是否允许加入购物车");
            entity.Property(value => value.SourceRevision).HasComment("已应用的目录商品修订号，用于拒绝乱序事件");
        });
    }
}
