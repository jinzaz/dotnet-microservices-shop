using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.BasketService.Infrastructure.Migrations
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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "baskets",
                type: "bigint",
                nullable: false,
                comment: "购物车乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "baskets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "购物车所属用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedOnUtc",
                table: "baskets",
                type: "timestamp with time zone",
                nullable: false,
                comment: "购物车最后变更的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "SourceRevision",
                table: "basket_product_snapshots",
                type: "bigint",
                nullable: false,
                comment: "已应用的目录商品修订号，用于拒绝乱序事件",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "basket_product_snapshots",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "目录服务发布的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "basket_product_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "目录商品当前单价的本地投影",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "basket_product_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "目录商品名称的本地投影",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<bool>(
                name: "IsAvailable",
                table: "basket_product_snapshots",
                type: "boolean",
                nullable: false,
                comment: "目录商品当前是否允许加入购物车",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "basket_product_snapshots",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                comment: "目录商品价格的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceAtSelection",
                table: "basket_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "最近选择或增量更新时的商品单价快照",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "basket_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "加入购物车时的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "basket_items",
                type: "integer",
                nullable: false,
                comment: "当前购买数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "ProductRevisionAtSelection",
                table: "basket_items",
                type: "bigint",
                nullable: false,
                comment: "商品快照对应的目录修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "ProductNameAtSelection",
                table: "basket_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "最近选择或增量更新时的商品名称快照",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyAtSelection",
                table: "basket_items",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                comment: "商品价格快照的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "basket_coupons",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "待在结算时校验的标准化优惠券码",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);
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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "baskets",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "购物车乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "baskets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "购物车所属用户标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedOnUtc",
                table: "baskets",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "购物车最后变更的 UTC 时间");

            migrationBuilder.AlterColumn<long>(
                name: "SourceRevision",
                table: "basket_product_snapshots",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "已应用的目录商品修订号，用于拒绝乱序事件");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "basket_product_snapshots",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "目录服务发布的标准化 SKU");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "basket_product_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "目录商品当前单价的本地投影");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "basket_product_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "目录商品名称的本地投影");

            migrationBuilder.AlterColumn<bool>(
                name: "IsAvailable",
                table: "basket_product_snapshots",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "目录商品当前是否允许加入购物车");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "basket_product_snapshots",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldComment: "目录商品价格的三位币种代码");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceAtSelection",
                table: "basket_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "最近选择或增量更新时的商品单价快照");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "basket_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "加入购物车时的标准化 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "basket_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "当前购买数量");

            migrationBuilder.AlterColumn<long>(
                name: "ProductRevisionAtSelection",
                table: "basket_items",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "商品快照对应的目录修订号");

            migrationBuilder.AlterColumn<string>(
                name: "ProductNameAtSelection",
                table: "basket_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "最近选择或增量更新时的商品名称快照");

            migrationBuilder.AlterColumn<string>(
                name: "CurrencyAtSelection",
                table: "basket_items",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(3)",
                oldFixedLength: true,
                oldMaxLength: 3,
                oldComment: "商品价格快照的三位币种代码");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "basket_coupons",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "待在结算时校验的标准化优惠券码");
        }
    }
}
