using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChineseCoreColumnComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "库存变动业务类型",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "stock_movements",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "发生库存变动的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "ReservedDelta",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "预占库存变化量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedAfter",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "变动后的预占库存余额",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "stock_movements",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true,
                comment: "幂等请求载荷的 SHA-256 摘要",
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ReferenceType",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "关联业务单据类型",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferenceId",
                table: "stock_movements",
                type: "uuid",
                nullable: true,
                comment: "关联的预占或秒杀活动标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "stock_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "库存变动原因",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "OperatorId",
                table: "stock_movements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "发起库存变动的主体标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OnHandDelta",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "实物库存变化量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "OnHandAfter",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                comment: "变动后的实物库存余额",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "stock_movements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "人工调整等请求的业务幂等键",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "stock_items",
                type: "bigint",
                nullable: false,
                comment: "库存乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "stock_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "标准化商品 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "Reserved",
                table: "stock_items",
                type: "integer",
                nullable: false,
                comment: "已预占但尚未扣减的库存量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "OnHand",
                table: "stock_items",
                type: "integer",
                nullable: false,
                comment: "当前实物库存总量",
                oldClrType: typeof(int),
                oldType: "integer");

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "inventory_reservations",
                type: "bigint",
                nullable: false,
                comment: "预占乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "inventory_reservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "秒杀预占所属用户；普通预占为空",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TerminalReason",
                table: "inventory_reservations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "预占释放的业务原因",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "inventory_reservations",
                type: "integer",
                nullable: false,
                comment: "预占状态：已预占、已提交或已释放",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReleasedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "库存释放的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: false,
                comment: "预占关联的订单标识，也是业务唯一键",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations",
                type: "bigint",
                nullable: true,
                comment: "秒杀库存桶标识；普通预占为空",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: true,
                comment: "秒杀活动标识；普通预占为空",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: false,
                comment: "预占自动释放的 UTC 截止时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CommittedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "库存正式扣减的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "inventory_reservation_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "预占商品的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "inventory_reservation_lines",
                type: "integer",
                nullable: false,
                comment: "该商品的预占数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "inventory_request_decisions",
                type: "integer",
                nullable: false,
                comment: "库存申请的持久化决策状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "inventory_request_decisions",
                type: "uuid",
                nullable: true,
                comment: "申请成功后生成的预占标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "inventory_request_decisions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                comment: "库存申请载荷的 SHA-256 摘要",
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "RejectionReason",
                table: "inventory_request_decisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "库存申请拒绝原因",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "inventory_request_decisions",
                type: "uuid",
                nullable: false,
                comment: "订单标识，也是库存申请的业务幂等键",
                oldClrType: typeof(Guid),
                oldType: "uuid");

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
                table: "flash_sale_stock_buckets",
                type: "bigint",
                nullable: false,
                comment: "库存桶乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "SoldQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                comment: "该桶已确认售出的库存量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                comment: "该桶已预占的库存量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ReleasedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                comment: "活动关闭后不再销售的库存量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "BucketNumber",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                comment: "活动内的库存桶编号",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "AllocatedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                comment: "分配给该桶的库存总量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "flash_sale_campaigns",
                type: "bigint",
                nullable: false,
                comment: "秒杀活动乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "TotalQuantity",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                comment: "活动分配的库存总量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Strategy",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                comment: "秒杀库存采用单行或分桶策略",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                comment: "秒杀活动状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsOnUtc",
                table: "flash_sale_campaigns",
                type: "timestamp with time zone",
                nullable: false,
                comment: "活动开始的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "flash_sale_campaigns",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "秒杀商品的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseLimit",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                comment: "每个用户在本活动中的购买上限",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsOnUtc",
                table: "flash_sale_campaigns",
                type: "timestamp with time zone",
                nullable: false,
                comment: "活动结束的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "BucketCount",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                comment: "用于分散热点写入的库存桶数量",
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "库存变动业务类型");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "stock_movements",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "发生库存变动的标准化 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedDelta",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "预占库存变化量");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedAfter",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "变动后的预占库存余额");

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "stock_movements",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64,
                oldNullable: true,
                oldComment: "幂等请求载荷的 SHA-256 摘要");

            migrationBuilder.AlterColumn<int>(
                name: "ReferenceType",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "关联业务单据类型");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferenceId",
                table: "stock_movements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "关联的预占或秒杀活动标识");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "stock_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "库存变动原因");

            migrationBuilder.AlterColumn<string>(
                name: "OperatorId",
                table: "stock_movements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "发起库存变动的主体标识");

            migrationBuilder.AlterColumn<int>(
                name: "OnHandDelta",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "实物库存变化量");

            migrationBuilder.AlterColumn<int>(
                name: "OnHandAfter",
                table: "stock_movements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "变动后的实物库存余额");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "stock_movements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "人工调整等请求的业务幂等键");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "stock_items",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "库存乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "stock_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "标准化商品 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "Reserved",
                table: "stock_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "已预占但尚未扣减的库存量");

            migrationBuilder.AlterColumn<int>(
                name: "OnHand",
                table: "stock_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "当前实物库存总量");

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

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "inventory_reservations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "预占乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "inventory_reservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "秒杀预占所属用户；普通预占为空");

            migrationBuilder.AlterColumn<string>(
                name: "TerminalReason",
                table: "inventory_reservations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "预占释放的业务原因");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "inventory_reservations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "预占状态：已预占、已提交或已释放");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ReleasedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "库存释放的 UTC 时间");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "预占关联的订单标识，也是业务唯一键");

            migrationBuilder.AlterColumn<long>(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "秒杀库存桶标识；普通预占为空");

            migrationBuilder.AlterColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "秒杀活动标识；普通预占为空");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "预占自动释放的 UTC 截止时间");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CommittedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "库存正式扣减的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "inventory_reservation_lines",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "预占商品的标准化 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "inventory_reservation_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "该商品的预占数量");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "inventory_request_decisions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "库存申请的持久化决策状态");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "inventory_request_decisions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "申请成功后生成的预占标识");

            migrationBuilder.AlterColumn<string>(
                name: "RequestHash",
                table: "inventory_request_decisions",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64,
                oldComment: "库存申请载荷的 SHA-256 摘要");

            migrationBuilder.AlterColumn<string>(
                name: "RejectionReason",
                table: "inventory_request_decisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "库存申请拒绝原因");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "inventory_request_decisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "订单标识，也是库存申请的业务幂等键");

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
                table: "flash_sale_stock_buckets",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "库存桶乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "SoldQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "该桶已确认售出的库存量");

            migrationBuilder.AlterColumn<int>(
                name: "ReservedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "该桶已预占的库存量");

            migrationBuilder.AlterColumn<int>(
                name: "ReleasedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "活动关闭后不再销售的库存量");

            migrationBuilder.AlterColumn<int>(
                name: "BucketNumber",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "活动内的库存桶编号");

            migrationBuilder.AlterColumn<int>(
                name: "AllocatedQuantity",
                table: "flash_sale_stock_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "分配给该桶的库存总量");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "flash_sale_campaigns",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "秒杀活动乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "TotalQuantity",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "活动分配的库存总量");

            migrationBuilder.AlterColumn<int>(
                name: "Strategy",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "秒杀库存采用单行或分桶策略");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "秒杀活动状态");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsOnUtc",
                table: "flash_sale_campaigns",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "活动开始的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "flash_sale_campaigns",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "秒杀商品的标准化 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseLimit",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "每个用户在本活动中的购买上限");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsOnUtc",
                table: "flash_sale_campaigns",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "活动结束的 UTC 时间");

            migrationBuilder.AlterColumn<int>(
                name: "BucketCount",
                table: "flash_sale_campaigns",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "用于分散热点写入的库存桶数量");
        }
    }
}
