using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
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
                name: "SourceRevision",
                table: "promotion_projections",
                type: "bigint",
                nullable: false,
                comment: "已应用的促销业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "DefinitionChecksum",
                table: "promotion_projections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "促销定义快照的 SHA-256 校验值",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "product_projections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "下单侧使用的目录单价投影",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "orders",
                type: "bigint",
                nullable: false,
                comment: "订单乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "下单用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<decimal>(
                name: "SubtotalAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "优惠前商品小计",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "orders",
                type: "integer",
                nullable: false,
                comment: "订单交易状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "SourceBasketVersion",
                table: "orders",
                type: "bigint",
                nullable: true,
                comment: "发起结算的购物车版本，用于结算幂等",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceBasketId",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "发起结算的购物车标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ShippingAddressId",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "用户地址簿中的来源地址标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "库存服务返回的预占标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestFingerprint",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "原始下单请求的 SHA-256 指纹",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "PricingSnapshotId",
                table: "orders",
                type: "uuid",
                nullable: false,
                comment: "本次定价快照标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "PricingSnapshotHash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "商品与优惠定价快照的 SHA-256 摘要",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "用户选择的支付方式",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentId",
                table: "orders",
                type: "uuid",
                nullable: true,
                comment: "支付服务返回的支付单标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PaymentDueOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                comment: "订单支付截止的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "orders",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                comment: "面向用户和运营的订单编号",
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "用户维度的下单幂等键",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "FulfillmentStatus",
                table: "orders",
                type: "integer",
                nullable: false,
                comment: "订单履约状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "订单优惠总额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "订单金额的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "订单取消原因",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "最终应付金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "分摊优惠后的成交单价",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "order_lines",
                type: "integer",
                nullable: false,
                comment: "下单商品数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "ListUnitPrice",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "下单时的目录单价快照",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "LineSubtotal",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "优惠前行小计",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "LineAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "订单行最终应付金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "订单行优惠金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "RequestFingerprint",
                table: "order_fulfillment_status_history",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "履约操作请求指纹",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "order_fulfillment_status_history",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "履约操作幂等键",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SnapshotJson",
                table: "order_discounts",
                type: "jsonb",
                nullable: false,
                comment: "用于审计和复算的促销定义快照",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<long>(
                name: "PromotionRevision",
                table: "order_discounts",
                type: "bigint",
                nullable: false,
                comment: "计算优惠时采用的促销修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "CouponId",
                table: "order_discounts",
                type: "uuid",
                nullable: true,
                comment: "优惠来源券标识；自动促销时为空",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "order_discounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "该优惠在订单上的抵扣金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "order_coupon_redemptions",
                type: "bigint",
                nullable: false,
                comment: "券核销记录乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "order_coupon_redemptions",
                type: "integer",
                nullable: false,
                comment: "券配额的预留、核销或释放状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "QuotaBucketId",
                table: "order_coupon_redemptions",
                type: "bigint",
                nullable: true,
                comment: "本次核销占用的配额桶标识",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "order_coupon_redemptions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "未支付订单释放券配额的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "order_coupon_redemptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "该优惠券为订单抵扣的金额",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "coupon_quota_buckets",
                type: "bigint",
                nullable: false,
                comment: "配额桶乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedCount",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                comment: "已为未完成订单预留的数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ConsumedCount",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                comment: "已由确认订单核销的数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Capacity",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                comment: "本地配额桶可核销总量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "SourceRevision",
                table: "coupon_projections",
                type: "bigint",
                nullable: false,
                comment: "已应用的优惠券业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "QuotaStrategy",
                table: "coupon_projections",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                comment: "券配额的单行或分桶并发策略",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "MaskedCode",
                table: "coupon_projections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "用于展示和审计的脱敏券码",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "CodeDigest",
                table: "coupon_projections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "标准化券码的 SHA-256 摘要",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
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
                name: "SourceRevision",
                table: "promotion_projections",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "已应用的促销业务修订号");

            migrationBuilder.AlterColumn<string>(
                name: "DefinitionChecksum",
                table: "promotion_projections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "促销定义快照的 SHA-256 校验值");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "product_projections",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "下单侧使用的目录单价投影");

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "orders",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "订单乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "下单用户标识");

            migrationBuilder.AlterColumn<decimal>(
                name: "SubtotalAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "优惠前商品小计");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "订单交易状态");

            migrationBuilder.AlterColumn<long>(
                name: "SourceBasketVersion",
                table: "orders",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "发起结算的购物车版本，用于结算幂等");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceBasketId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "发起结算的购物车标识");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShippingAddressId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "用户地址簿中的来源地址标识");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "库存服务返回的预占标识");

            migrationBuilder.AlterColumn<string>(
                name: "RequestFingerprint",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "原始下单请求的 SHA-256 指纹");

            migrationBuilder.AlterColumn<Guid>(
                name: "PricingSnapshotId",
                table: "orders",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "本次定价快照标识");

            migrationBuilder.AlterColumn<string>(
                name: "PricingSnapshotHash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "商品与优惠定价快照的 SHA-256 摘要");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "用户选择的支付方式");

            migrationBuilder.AlterColumn<Guid>(
                name: "PaymentId",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "支付服务返回的支付单标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PaymentDueOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "订单支付截止的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "orders",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48,
                oldComment: "面向用户和运营的订单编号");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "用户维度的下单幂等键");

            migrationBuilder.AlterColumn<int>(
                name: "FulfillmentStatus",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "订单履约状态");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "订单优惠总额");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "订单金额的三位币种代码");

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "订单取消原因");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "最终应付金额");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "分摊优惠后的成交单价");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "order_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "下单商品数量");

            migrationBuilder.AlterColumn<decimal>(
                name: "ListUnitPrice",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "下单时的目录单价快照");

            migrationBuilder.AlterColumn<decimal>(
                name: "LineSubtotal",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "优惠前行小计");

            migrationBuilder.AlterColumn<decimal>(
                name: "LineAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "订单行最终应付金额");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "订单行优惠金额");

            migrationBuilder.AlterColumn<string>(
                name: "RequestFingerprint",
                table: "order_fulfillment_status_history",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldComment: "履约操作请求指纹");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "order_fulfillment_status_history",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "履约操作幂等键");

            migrationBuilder.AlterColumn<string>(
                name: "SnapshotJson",
                table: "order_discounts",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "用于审计和复算的促销定义快照");

            migrationBuilder.AlterColumn<long>(
                name: "PromotionRevision",
                table: "order_discounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "计算优惠时采用的促销修订号");

            migrationBuilder.AlterColumn<Guid>(
                name: "CouponId",
                table: "order_discounts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "优惠来源券标识；自动促销时为空");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "order_discounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "该优惠在订单上的抵扣金额");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "order_coupon_redemptions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "券核销记录乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "order_coupon_redemptions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "券配额的预留、核销或释放状态");

            migrationBuilder.AlterColumn<long>(
                name: "QuotaBucketId",
                table: "order_coupon_redemptions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "本次核销占用的配额桶标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "order_coupon_redemptions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "未支付订单释放券配额的 UTC 时间");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountAmount",
                table: "order_coupon_redemptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "该优惠券为订单抵扣的金额");

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "coupon_quota_buckets",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "配额桶乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedCount",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "已为未完成订单预留的数量");

            migrationBuilder.AlterColumn<int>(
                name: "ConsumedCount",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "已由确认订单核销的数量");

            migrationBuilder.AlterColumn<int>(
                name: "Capacity",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "本地配额桶可核销总量");

            migrationBuilder.AlterColumn<long>(
                name: "SourceRevision",
                table: "coupon_projections",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "已应用的优惠券业务修订号");

            migrationBuilder.AlterColumn<string>(
                name: "QuotaStrategy",
                table: "coupon_projections",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldComment: "券配额的单行或分桶并发策略");

            migrationBuilder.AlterColumn<string>(
                name: "MaskedCode",
                table: "coupon_projections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "用于展示和审计的脱敏券码");

            migrationBuilder.AlterColumn<string>(
                name: "CodeDigest",
                table: "coupon_projections",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "标准化券码的 SHA-256 摘要");
        }
    }
}
