using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSA.PaymentService.Domain;

namespace MSA.PaymentService.Infrastructure;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> entity)
    {
        entity.ToTable("payments", table =>
        {
            table.HasCheckConstraint("CK_payments_amount", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_payments_currency",
                "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");
            table.HasCheckConstraint("CK_payments_channel", "\"Channel\" IN (0, 1)");
            table.HasCheckConstraint("CK_payments_status", "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint(
                "CK_payments_succeeded_fields",
                "\"Status\" NOT IN (1, 3, 4) OR (\"ProviderTransactionId\" IS NOT NULL AND \"PaidOnUtc\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_payments_failure_fields",
                "\"Status\" NOT IN (2, 6) OR \"FailureReason\" IS NOT NULL");
            table.HasCheckConstraint("CK_payments_version", "\"Version\" >= 0");
            table.HasCheckConstraint(
                "CK_payments_refunded_amount",
                "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");
            table.HasCheckConstraint(
                "CK_payments_refunded_status",
                "\"Status\" <> 4 OR \"RefundedAmount\" = \"Amount\"");
            table.HasCheckConstraint(
                "CK_payments_closed_fields",
                "\"Status\" <> 5 OR \"ClosedOnUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_payments_pricing_snapshot",
                "(\"PricingSnapshotId\" IS NULL AND \"PricingSnapshotHash\" IS NULL) OR (\"PricingSnapshotId\" IS NOT NULL AND \"PricingSnapshotHash\" ~ '^[0-9A-F]{64}$')");
        });

        entity.HasKey(value => value.Id);
        entity.HasIndex(value => value.OrderId).IsUnique();
        entity.HasIndex(value => value.MerchantTradeNo).IsUnique();
        entity.HasIndex(value => new { value.Channel, value.ProviderTransactionId })
            .IsUnique()
            .HasFilter("\"ProviderTransactionId\" IS NOT NULL");
        entity.HasIndex(value => new { value.ExpiresOnUtc, value.Id })
            .HasDatabaseName("IX_payments_pending_expiry")
            .HasFilter("\"Status\" = 0");
        entity.Property(value => value.OrderId).HasComment("支付单关联的订单标识，也是业务唯一键");
        entity.Property(value => value.UserId).HasMaxLength(200).HasComment("付款用户标识");
        entity.Property(value => value.Amount).HasPrecision(18, 2).HasComment("订单应支付金额");
        entity.Property(value => value.RefundedAmount).HasPrecision(18, 2).HasComment("累计退款成功金额");
        entity.Property(value => value.Currency).HasMaxLength(3).HasComment("支付金额的三位币种代码");
        entity.Property(value => value.Channel).HasComment("支付渠道");
        entity.Property(value => value.Status).HasComment("支付单业务状态");
        entity.Property(value => value.MerchantTradeNo).HasMaxLength(64).HasComment("提交给支付渠道的商户交易号");
        entity.Property(value => value.ProviderTransactionId).HasMaxLength(128).HasComment("支付渠道返回的交易标识");
        entity.Property(value => value.PaymentUrl).HasMaxLength(4096);
        entity.Property(value => value.FailureReason).HasMaxLength(2000).HasComment("最近一次支付或退款失败原因");
        entity.Property(value => value.ExpiresOnUtc).HasComment("待支付单失效的 UTC 时间");
        entity.Property(value => value.PricingSnapshotId).HasComment("订单侧定价快照标识");
        entity.Property(value => value.PricingSnapshotHash).HasMaxLength(128).HasComment("订单侧定价快照的 SHA-256 摘要");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("支付单乐观并发版本号");
    }
}

internal sealed class PaymentProviderOperationConfiguration
    : IEntityTypeConfiguration<PaymentProviderOperation>
{
    public void Configure(EntityTypeBuilder<PaymentProviderOperation> entity)
    {
        entity.ToTable("payment_provider_operations", table =>
        {
            table.HasCheckConstraint("CK_payment_provider_operations_type", "\"Type\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_payment_provider_operations_status", "\"Status\" IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_payment_provider_operations_attempts", "\"AttemptCount\" >= 0");
            table.HasCheckConstraint(
                "CK_payment_provider_operations_lease",
                "(\"Status\" = 1 AND \"LockId\" IS NOT NULL AND \"LockedUntilUtc\" IS NOT NULL) OR "
                + "(\"Status\" <> 1 AND \"LockId\" IS NULL AND \"LockedUntilUtc\" IS NULL)");
            table.HasCheckConstraint(
                "CK_payment_provider_operations_completion",
                "\"Status\" <> 2 OR \"CompletedOnUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_payment_provider_operations_reference",
                "(\"Type\" = 2 AND \"BusinessReferenceId\" IS NOT NULL) OR "
                + "(\"Type\" <> 2 AND \"BusinessReferenceId\" IS NULL)");
            table.HasCheckConstraint("CK_payment_provider_operations_version", "\"Version\" >= 0");
        });

        entity.HasKey(value => value.Id);
        entity.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(value => value.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PaymentRefund>()
            .WithMany()
            .HasForeignKey(value => value.BusinessReferenceId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => value.IdempotencyKey).IsUnique();
        entity.HasIndex(value => new { value.PaymentId, value.Type })
            .IsUnique()
            .HasDatabaseName("IX_payment_provider_operations_singleton")
            .HasFilter("\"Type\" IN (0, 1)");
        entity.HasIndex(value => new { value.NextAttemptOnUtc, value.CreatedOnUtc, value.Id })
            .HasDatabaseName("IX_payment_provider_operations_pending")
            .HasFilter("\"Status\" = 0");
        entity.HasIndex(value => new { value.LockedUntilUtc, value.Id })
            .HasDatabaseName("IX_payment_provider_operations_expired_lease")
            .HasFilter("\"Status\" = 1");
        entity.Property(value => value.Type).HasComment("渠道操作类型：创建、关闭或退款");
        entity.Property(value => value.IdempotencyKey).HasMaxLength(100).HasComment("渠道调用的稳定幂等键");
        entity.Property(value => value.BusinessReferenceId).HasComment("退款操作关联的退款单标识");
        entity.Property(value => value.Status).HasComment("渠道操作调度状态");
        entity.Property(value => value.AttemptCount).HasComment("已执行的渠道调用次数");
        entity.Property(value => value.NextAttemptOnUtc).HasComment("下一次允许调用渠道的 UTC 时间");
        entity.Property(value => value.LockId).HasComment("当前执行实例持有的租约标识");
        entity.Property(value => value.LockedUntilUtc).HasComment("渠道操作租约到期的 UTC 时间");
        entity.Property(value => value.Reason).HasMaxLength(2000);
        entity.Property(value => value.LastError).HasMaxLength(4000).HasComment("最近一次渠道调用失败原因");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("渠道操作乐观并发版本号");
    }
}

internal sealed class PaymentNotificationConfiguration
    : IEntityTypeConfiguration<PaymentNotificationRecord>
{
    public void Configure(EntityTypeBuilder<PaymentNotificationRecord> entity)
    {
        entity.ToTable("payment_notifications", table =>
        {
            table.HasCheckConstraint("CK_payment_notifications_amount", "\"Amount\" >= 0");
            table.HasCheckConstraint(
                "CK_payment_notifications_currency",
                "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");
            table.HasCheckConstraint("CK_payment_notifications_channel", "\"Channel\" IN (0, 1)");
            table.HasCheckConstraint("CK_payment_notifications_status", "\"Status\" IN (0, 1, 2)");
            table.HasCheckConstraint(
                "CK_payment_notifications_processed",
                "\"Status\" = 0 OR \"ProcessedOnUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_payment_notifications_conflict",
                "\"Status\" <> 2 OR \"ConflictReason\" IS NOT NULL");
        });

        entity.HasKey(value => value.Id);
        entity.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(value => value.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => new { value.Channel, value.NotificationKey }).IsUnique();
        entity.HasIndex(value => new { value.Channel, value.ProviderNotificationId })
            .IsUnique()
            .HasFilter("\"ProviderNotificationId\" IS NOT NULL");
        entity.HasIndex(value => new { value.PaymentId, value.ReceivedOnUtc });
        entity.Property(value => value.NotificationKey).HasMaxLength(200).HasComment("渠道回调的业务去重键");
        entity.Property(value => value.ProviderNotificationId).HasMaxLength(128).HasComment("支付渠道的回调通知标识");
        entity.Property(value => value.MerchantTradeNo).HasMaxLength(64).HasComment("回调携带的商户交易号");
        entity.Property(value => value.ProviderTransactionId).HasMaxLength(128).HasComment("回调携带的渠道交易标识");
        entity.Property(value => value.Amount).HasPrecision(18, 2).HasComment("回调声明的支付金额");
        entity.Property(value => value.Currency).HasMaxLength(3).HasComment("回调声明的三位币种代码");
        entity.Property(value => value.Fingerprint).HasMaxLength(64).IsFixedLength().HasComment("规范化回调载荷的 SHA-256 指纹");
        entity.Property(value => value.Status).HasComment("回调处理状态");
        entity.Property(value => value.ConflictReason).HasMaxLength(2000).HasComment("重复通知载荷冲突或金额校验失败原因");
    }
}

internal sealed class PaymentStatusHistoryConfiguration
    : IEntityTypeConfiguration<PaymentStatusHistory>
{
    public void Configure(EntityTypeBuilder<PaymentStatusHistory> entity)
    {
        entity.ToTable("payment_status_history", table =>
        {
            table.HasCheckConstraint(
                "CK_payment_status_history_from_status",
                "\"FromStatus\" IS NULL OR \"FromStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
            table.HasCheckConstraint(
                "CK_payment_status_history_to_status",
                "\"ToStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
        });

        entity.HasKey(value => value.Id);
        entity.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(value => value.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => new { value.PaymentId, value.OccurredOnUtc });
        entity.Property(value => value.ReasonCode).HasMaxLength(80).HasComment("支付状态变更原因码");
        entity.Property(value => value.Details).HasMaxLength(2000);
    }
}

internal sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> entity)
    {
        entity.ToTable("payment_refunds", table =>
        {
            table.HasCheckConstraint("CK_payment_refunds_amount", "\"Amount\" > 0");
            table.HasCheckConstraint(
                "CK_payment_refunds_request_id",
                "\"RefundRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_payment_refunds_status", "\"Status\" IN (0, 1, 2)");
            table.HasCheckConstraint(
                "CK_payment_refunds_currency",
                "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");
            table.HasCheckConstraint(
                "CK_payment_refunds_completion",
                "\"Status\" = 0 OR \"CompletedOnUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_payment_refunds_failure",
                "\"Status\" <> 2 OR \"FailureReason\" IS NOT NULL");
            table.HasCheckConstraint("CK_payment_refunds_version", "\"Version\" >= 0");
        });

        entity.HasKey(value => value.Id);
        entity.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(value => value.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => value.RefundRequestId).IsUnique();
        entity.HasIndex(value => value.RequestMessageId).IsUnique();
        entity.HasIndex(value => value.MerchantRefundNo).IsUnique();
        entity.HasIndex(value => value.PaymentId)
            .IsUnique()
            .HasDatabaseName("IX_payment_refunds_one_pending_per_payment")
            .HasFilter("\"Status\" = 0");
        entity.HasIndex(value => new { value.PaymentId, value.Status, value.CreatedOnUtc });
        entity.Property(value => value.RefundRequestId).HasComment("上游退款请求的业务幂等标识");
        entity.Property(value => value.RequestMessageId).HasComment("触发退款的集成事件标识");
        entity.Property(value => value.MerchantRefundNo).HasMaxLength(64).HasComment("提交给支付渠道的商户退款号");
        entity.Property(value => value.Amount).HasPrecision(18, 2).HasComment("本次申请退款金额");
        entity.Property(value => value.Currency).HasMaxLength(3);
        entity.Property(value => value.Reason).HasMaxLength(500);
        entity.Property(value => value.Status).HasComment("退款单业务状态");
        entity.Property(value => value.ProviderRefundId).HasMaxLength(128).HasComment("支付渠道返回的退款标识");
        entity.Property(value => value.FailureReason).HasMaxLength(2000).HasComment("退款失败原因");
        entity.Property(value => value.Version).IsConcurrencyToken().HasComment("退款单乐观并发版本号");
    }
}

internal sealed class PaymentCancellationIntentConfiguration
    : IEntityTypeConfiguration<PaymentCancellationIntent>
{
    public void Configure(EntityTypeBuilder<PaymentCancellationIntent> entity)
    {
        entity.ToTable("payment_cancellation_intents", table =>
            table.HasCheckConstraint(
                "CK_payment_cancellation_intents_order",
                "\"OrderId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        entity.HasKey(value => value.OrderId);
        entity.HasIndex(value => value.RequestMessageId).IsUnique();
        entity.HasIndex(value => value.RequestedOnUtc);
        entity.Property(value => value.RequestMessageId).HasComment("触发取消的集成事件标识，用于消息幂等");
        entity.Property(value => value.Reason).HasMaxLength(2000).HasComment("请求关闭支付单的业务原因");
    }
}
