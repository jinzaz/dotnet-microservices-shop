using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.OrderService.Domain;
using static MSA.Persistence.Postgres.CheckSql;

namespace MSA.OrderService.Infrastructure.EntityConfigurations;

internal sealed class AfterSalesRequestConfiguration : IEntityTypeConfiguration<AfterSalesRequest>
{
    public void Configure(EntityTypeBuilder<AfterSalesRequest> entity)
    {
        entity.ToTable("order_after_sales_requests", table =>
        {
            table.HasCheckConstraint("CK_after_sales_refund_amount", "\"RefundAmount\" >= 0");
            table.HasCheckConstraint("CK_after_sales_currency", CurrencyCode(nameof(AfterSalesRequest.Currency)));
        });
        entity.HasKey(request => request.Id);
        entity.HasIndex(request => request.RequestNumber).IsUnique();
        entity.HasIndex(request => new { request.UserId, request.IdempotencyKey }).IsUnique();
        entity.HasIndex(request => new { request.UserId, request.CreatedOnUtc });
        entity.HasIndex(request => request.OrderId);
        entity.HasIndex(request => new { request.Status, request.StatusChangedOnUtc });
        entity.HasIndex(request => request.RefundRequestId).IsUnique().HasFilter("\"RefundRequestId\" IS NOT NULL");
        entity.Ignore(request => request.IsActive);
        entity.Ignore(request => request.IsOpen);
        entity.Ignore(request => request.IsRefundType);
        entity.Property(request => request.RequestNumber).HasMaxLength(48).HasComment("面向用户和运营的售后单号");
        entity.Property(request => request.OrderId).HasComment("关联订单标识");
        entity.Property(request => request.OrderNumber).HasMaxLength(48).HasComment("关联订单编号快照");
        entity.Property(request => request.UserId).HasMaxLength(200).HasComment("申请售后的用户标识");
        entity.Property(request => request.PaymentId).HasComment("原路退款使用的支付单标识");
        entity.Property(request => request.IdempotencyKey).HasMaxLength(100).HasComment("用户维度的售后申请幂等键");
        entity.Property(request => request.Type).HasComment("售后类型：仅退款、退货退款、换货、补寄");
        entity.Property(request => request.Status).HasComment("售后状态");
        entity.Property(request => request.Reason).HasComment("售后原因编码");
        entity.Property(request => request.Description).HasMaxLength(500).HasComment("买家补充说明");
        entity.Property(request => request.EvidenceObjectKeys).HasComment("私有桶中凭证图片的对象键");
        entity.Property(request => request.RefundAmount).HasPrecision(18, 2).HasComment("按实付折算的退款金额，换货与补寄为 0");
        entity.Property(request => request.Currency).HasMaxLength(3).HasComment("退款币种");
        entity.Property(request => request.ReturnAddress).HasMaxLength(500).HasComment("商家审核时提供的退货地址");
        entity.Property(request => request.ReturnCarrierName).HasMaxLength(120).HasComment("买家寄回的物流公司");
        entity.Property(request => request.ReturnTrackingNumber).HasMaxLength(120).HasComment("买家寄回的运单号");
        entity.Property(request => request.ReplacementCarrierCode).HasMaxLength(50);
        entity.Property(request => request.ReplacementCarrierName).HasMaxLength(120).HasComment("换货或补寄的物流公司");
        entity.Property(request => request.ReplacementTrackingNumber).HasMaxLength(120).HasComment("换货或补寄的运单号");
        entity.Property(request => request.RejectionReason).HasMaxLength(500).HasComment("拒绝时给买家的回复");
        entity.Property(request => request.RefundRequestId).HasComment("当前退款尝试的幂等标识，失败重试会换新");
        entity.Property(request => request.RefundFailureReason).HasMaxLength(1000).HasComment("支付渠道返回的退款失败原因");
        entity.Property(request => request.StatusChangedOnUtc).HasComment("进入当前状态的时间，超时自动处理从此起算");
        entity.Property(request => request.ReplacementStock).HasComment("换货/补寄新品的库存锁定状态");
        entity.Property(request => request.ReplacementStockFailure).HasMaxLength(500).HasComment("新品库存锁定失败原因");
        entity.Property(request => request.ReturnDisposition).HasComment("退回商品的入库处理结果");
        entity.Property(request => request.DispositionNote).HasMaxLength(500).HasComment("未入库原因等入库处理说明");
        entity.Property(request => request.DisposedBy).HasMaxLength(200).HasComment("入库处理操作人");
        entity.Property(request => request.Version).IsConcurrencyToken().HasComment("售后单乐观并发版本号");

        entity.HasMany(request => request.Lines)
            .WithOne()
            .HasForeignKey("RequestId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(request => request.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasMany(request => request.History)
            .WithOne()
            .HasForeignKey(history => history.RequestId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(request => request.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AfterSalesLineConfiguration : IEntityTypeConfiguration<AfterSalesLine>
{
    public void Configure(EntityTypeBuilder<AfterSalesLine> entity)
    {
        entity.ToTable("order_after_sales_lines", table =>
            table.HasCheckConstraint("CK_after_sales_lines_amounts", "\"Quantity\" > 0 AND \"RefundAmount\" >= 0"));
        entity.HasKey(line => line.Id);
        entity.Property<Guid>("RequestId");
        entity.HasIndex("RequestId", nameof(AfterSalesLine.Sku)).IsUnique();
        entity.Property(line => line.Sku).HasMaxLength(80);
        entity.Property(line => line.ProductName).HasMaxLength(200).HasComment("商品名称快照");
        entity.Property(line => line.VariantName).HasMaxLength(200).HasComment("规格名称快照");
        entity.Property(line => line.Quantity).HasComment("申请售后的件数");
        entity.Property(line => line.RefundAmount).HasPrecision(18, 2).HasComment("该行折算的退款金额");
        entity.Property(line => line.RestockedQuantity).HasComment("验货后重新入库的件数，未处理为空");
    }
}

internal sealed class AfterSalesHistoryConfiguration : IEntityTypeConfiguration<AfterSalesHistory>
{
    public void Configure(EntityTypeBuilder<AfterSalesHistory> entity)
    {
        entity.ToTable("order_after_sales_history");
        entity.HasKey(history => history.Id);
        entity.HasIndex(history => new { history.RequestId, history.OccurredOnUtc });
        entity.Property(history => history.FromStatus).HasComment("变更前售后状态");
        entity.Property(history => history.ToStatus).HasComment("变更后售后状态");
        entity.Property(history => history.Note).HasMaxLength(1000).HasComment("给买家展示的处理说明");
        entity.Property(history => history.Actor).HasMaxLength(200).HasComment("操作人：买家、后台操作员或 system");
    }
}
