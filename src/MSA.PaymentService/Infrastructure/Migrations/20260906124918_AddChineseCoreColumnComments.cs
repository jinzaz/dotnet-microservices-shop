using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChineseCoreColumnComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "saga_states",
                type: "bigint",
                nullable: false,
                comment: "乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TimeoutOnUtc",
                table: "saga_states",
                type: "timestamp with time zone",
                nullable: true,
                comment: "触发超时处理的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TimeoutLockedUntilUtc",
                table: "saga_states",
                type: "timestamp with time zone",
                nullable: true,
                comment: "超时处理租约到期的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TimeoutLockId",
                table: "saga_states",
                type: "uuid",
                nullable: true,
                comment: "当前超时处理任务的租约标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "saga_states",
                type: "integer",
                nullable: false,
                comment: "Saga 执行状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "saga_states",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "当前业务步骤",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "SagaType",
                table: "saga_states",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "Saga 流程类型",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "saga_states",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "流程失败或补偿失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Data",
                table: "saga_states",
                type: "jsonb",
                nullable: false,
                comment: "流程恢复与补偿所需的状态快照",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrelationId",
                table: "saga_states",
                type: "uuid",
                nullable: false,
                comment: "跨服务流程的关联标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payments",
                type: "bigint",
                nullable: false,
                comment: "支付单乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "付款用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payments",
                type: "integer",
                nullable: false,
                comment: "支付单业务状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "累计退款成功金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "支付渠道返回的交易标识",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PricingSnapshotId",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "订单侧定价快照标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PricingSnapshotHash",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "订单侧定价快照的 SHA-256 摘要",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "payments",
                type: "uuid",
                nullable: false,
                comment: "支付单关联的订单标识，也是业务唯一键",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "提交给支付渠道的商户交易号",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                comment: "最近一次支付或退款失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "待支付单失效的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "支付金额的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<int>(
                name: "Channel",
                table: "payments",
                type: "integer",
                nullable: false,
                comment: "支付渠道",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "订单应支付金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "ReasonCode",
                table: "payment_status_history",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "支付状态变更原因码",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payment_refunds",
                type: "bigint",
                nullable: false,
                comment: "退款单乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_refunds",
                type: "integer",
                nullable: false,
                comment: "退款单业务状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestMessageId",
                table: "payment_refunds",
                type: "uuid",
                nullable: false,
                comment: "触发退款的集成事件标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "RefundRequestId",
                table: "payment_refunds",
                type: "uuid",
                nullable: false,
                comment: "上游退款请求的业务幂等标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderRefundId",
                table: "payment_refunds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "支付渠道返回的退款标识",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MerchantRefundNo",
                table: "payment_refunds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "提交给支付渠道的商户退款号",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payment_refunds",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                comment: "退款失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payment_refunds",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "本次申请退款金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payment_provider_operations",
                type: "bigint",
                nullable: false,
                comment: "渠道操作乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                comment: "渠道操作类型：创建、关闭或退款",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                comment: "渠道操作调度状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptOnUtc",
                table: "payment_provider_operations",
                type: "timestamp with time zone",
                nullable: false,
                comment: "下一次允许调用渠道的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockedUntilUtc",
                table: "payment_provider_operations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "渠道操作租约到期的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LockId",
                table: "payment_provider_operations",
                type: "uuid",
                nullable: true,
                comment: "当前执行实例持有的租约标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "payment_provider_operations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "最近一次渠道调用失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "payment_provider_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "渠道调用的稳定幂等键",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<Guid>(
                name: "BusinessReferenceId",
                table: "payment_provider_operations",
                type: "uuid",
                nullable: true,
                comment: "退款操作关联的退款单标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                comment: "已执行的渠道调用次数",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_notifications",
                type: "integer",
                nullable: false,
                comment: "回调处理状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payment_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "回调携带的渠道交易标识",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderNotificationId",
                table: "payment_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "支付渠道的回调通知标识",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NotificationKey",
                table: "payment_notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "渠道回调的业务去重键",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payment_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "回调携带的商户交易号",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Fingerprint",
                table: "payment_notifications",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                comment: "规范化回调载荷的 SHA-256 指纹",
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "payment_notifications",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "回调声明的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "ConflictReason",
                table: "payment_notifications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                comment: "重复通知载荷冲突或金额校验失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payment_notifications",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "回调声明的支付金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestMessageId",
                table: "payment_cancellation_intents",
                type: "uuid",
                nullable: false,
                comment: "触发取消的集成事件标识，用于消息幂等",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "payment_cancellation_intents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "请求关闭支付单的业务原因",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "outbox_messages",
                type: "bigint",
                nullable: false,
                comment: "乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                comment: "消息发布状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                comment: "累计发布重试次数",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "outbox_messages",
                type: "jsonb",
                nullable: false,
                comment: "待发布事件的 JSON 载荷",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptOnUtc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "下一次允许尝试发布的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockedUntilUtc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true,
                comment: "发布租约到期的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "LockId",
                table: "outbox_messages",
                type: "uuid",
                nullable: true,
                comment: "当前发布任务的租约标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "outbox_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "最近一次发布失败原因",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                comment: "集成事件的契约类型名称",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ProcessedOnUtc",
                table: "inbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "首次成功登记消费的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Consumer",
                table: "inbox_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "消费处理器的稳定名称",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                table: "inbox_messages",
                type: "uuid",
                nullable: false,
                comment: "已消费的集成事件标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "saga_states",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "乐观并发版本号");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TimeoutOnUtc",
                table: "saga_states",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "触发超时处理的 UTC 时间");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TimeoutLockedUntilUtc",
                table: "saga_states",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "超时处理租约到期的 UTC 时间");

            migrationBuilder.AlterColumn<Guid>(
                name: "TimeoutLockId",
                table: "saga_states",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "当前超时处理任务的租约标识");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "saga_states",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Saga 执行状态");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                table: "saga_states",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "当前业务步骤");

            migrationBuilder.AlterColumn<string>(
                name: "SagaType",
                table: "saga_states",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "Saga 流程类型");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "saga_states",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true,
                oldComment: "流程失败或补偿失败原因");

            migrationBuilder.AlterColumn<string>(
                name: "Data",
                table: "saga_states",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "流程恢复与补偿所需的状态快照");

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrelationId",
                table: "saga_states",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "跨服务流程的关联标识");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "支付单乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "付款用户标识");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "支付单业务状态");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "累计退款成功金额");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldComment: "支付渠道返回的交易标识");

            migrationBuilder.AlterColumn<Guid>(
                name: "PricingSnapshotId",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "订单侧定价快照标识");

            migrationBuilder.AlterColumn<string>(
                name: "PricingSnapshotHash",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldComment: "订单侧定价快照的 SHA-256 摘要");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "payments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "支付单关联的订单标识，也是业务唯一键");

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "提交给支付渠道的商户交易号");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true,
                oldComment: "最近一次支付或退款失败原因");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "待支付单失效的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "支付金额的三位币种代码");

            migrationBuilder.AlterColumn<int>(
                name: "Channel",
                table: "payments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "支付渠道");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "订单应支付金额");

            migrationBuilder.AlterColumn<string>(
                name: "ReasonCode",
                table: "payment_status_history",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "支付状态变更原因码");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payment_refunds",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "退款单乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_refunds",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "退款单业务状态");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestMessageId",
                table: "payment_refunds",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "触发退款的集成事件标识");

            migrationBuilder.AlterColumn<Guid>(
                name: "RefundRequestId",
                table: "payment_refunds",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "上游退款请求的业务幂等标识");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderRefundId",
                table: "payment_refunds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldComment: "支付渠道返回的退款标识");

            migrationBuilder.AlterColumn<string>(
                name: "MerchantRefundNo",
                table: "payment_refunds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "提交给支付渠道的商户退款号");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payment_refunds",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true,
                oldComment: "退款失败原因");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payment_refunds",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "本次申请退款金额");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "payment_provider_operations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "渠道操作乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "渠道操作类型：创建、关闭或退款");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "渠道操作调度状态");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptOnUtc",
                table: "payment_provider_operations",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "下一次允许调用渠道的 UTC 时间");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockedUntilUtc",
                table: "payment_provider_operations",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "渠道操作租约到期的 UTC 时间");

            migrationBuilder.AlterColumn<Guid>(
                name: "LockId",
                table: "payment_provider_operations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "当前执行实例持有的租约标识");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "payment_provider_operations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true,
                oldComment: "最近一次渠道调用失败原因");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "payment_provider_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "渠道调用的稳定幂等键");

            migrationBuilder.AlterColumn<Guid>(
                name: "BusinessReferenceId",
                table: "payment_provider_operations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "退款操作关联的退款单标识");

            migrationBuilder.AlterColumn<int>(
                name: "AttemptCount",
                table: "payment_provider_operations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "已执行的渠道调用次数");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "payment_notifications",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "回调处理状态");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payment_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "回调携带的渠道交易标识");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderNotificationId",
                table: "payment_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldComment: "支付渠道的回调通知标识");

            migrationBuilder.AlterColumn<string>(
                name: "NotificationKey",
                table: "payment_notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "渠道回调的业务去重键");

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payment_notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "回调携带的商户交易号");

            migrationBuilder.AlterColumn<string>(
                name: "Fingerprint",
                table: "payment_notifications",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64,
                oldComment: "规范化回调载荷的 SHA-256 指纹");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "payment_notifications",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "回调声明的三位币种代码");

            migrationBuilder.AlterColumn<string>(
                name: "ConflictReason",
                table: "payment_notifications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true,
                oldComment: "重复通知载荷冲突或金额校验失败原因");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "payment_notifications",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "回调声明的支付金额");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestMessageId",
                table: "payment_cancellation_intents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "触发取消的集成事件标识，用于消息幂等");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "payment_cancellation_intents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "请求关闭支付单的业务原因");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "outbox_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "消息发布状态");

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "累计发布重试次数");

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "outbox_messages",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "待发布事件的 JSON 载荷");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptOnUtc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "下一次允许尝试发布的 UTC 时间");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockedUntilUtc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "发布租约到期的 UTC 时间");

            migrationBuilder.AlterColumn<Guid>(
                name: "LockId",
                table: "outbox_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "当前发布任务的租约标识");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "outbox_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true,
                oldComment: "最近一次发布失败原因");

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "集成事件的契约类型名称");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ProcessedOnUtc",
                table: "inbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "首次成功登记消费的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "Consumer",
                table: "inbox_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "消费处理器的稳定名称");

            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                table: "inbox_messages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "已消费的集成事件标识");
        }
    }
}
