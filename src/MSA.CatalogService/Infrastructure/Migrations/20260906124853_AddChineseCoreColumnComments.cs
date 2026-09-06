using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
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
                table: "promotions",
                type: "bigint",
                nullable: false,
                comment: "促销乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "TriggerType",
                table: "promotions",
                type: "integer",
                nullable: false,
                comment: "促销触发方式：自动或优惠券",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "promotions",
                type: "integer",
                nullable: false,
                comment: "促销生命周期状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsOnUtc",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "促销窗口开始的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "StackPolicy",
                table: "promotions",
                type: "integer",
                nullable: false,
                comment: "促销是否允许与其他优惠叠加",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "promotions",
                type: "bigint",
                nullable: false,
                comment: "促销定义的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "PublishedRevision",
                table: "promotions",
                type: "bigint",
                nullable: true,
                comment: "当前已发布定义对应的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "promotions",
                type: "integer",
                nullable: false,
                comment: "多促销候选时的计算优先级",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                table: "promotions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                comment: "用于唯一匹配的标准化促销编码",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "ExclusiveGroup",
                table: "promotions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                comment: "互斥促销分组标识",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsOnUtc",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "促销窗口结束的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "DefinitionChecksum",
                table: "promotions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "已发布促销定义的 SHA-256 校验值",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "promotions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                comment: "促销活动业务编码",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "TargetType",
                table: "promotion_targets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "促销目标范围类型",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "TargetKey",
                table: "promotion_targets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "目标商品、SKU 或分类的业务键",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_targets",
                type: "integer",
                nullable: false,
                comment: "同类组件的稳定计算顺序",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_targets",
                type: "integer",
                nullable: false,
                comment: "组件配置结构版本",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsExcluded",
                table: "promotion_targets",
                type: "boolean",
                nullable: false,
                comment: "是否从促销目标范围中排除",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_targets",
                type: "jsonb",
                nullable: false,
                comment: "规则、权益或目标的规范化 JSON 配置",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_rules",
                type: "integer",
                nullable: false,
                comment: "同类组件的稳定计算顺序",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_rules",
                type: "integer",
                nullable: false,
                comment: "组件配置结构版本",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "RuleType",
                table: "promotion_rules",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "促销适用条件类型",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_rules",
                type: "jsonb",
                nullable: false,
                comment: "规则、权益或目标的规范化 JSON 配置",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_benefits",
                type: "integer",
                nullable: false,
                comment: "同类组件的稳定计算顺序",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_benefits",
                type: "integer",
                nullable: false,
                comment: "组件配置结构版本",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_benefits",
                type: "jsonb",
                nullable: false,
                comment: "规则、权益或目标的规范化 JSON 配置",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "BenefitType",
                table: "promotion_benefits",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "促销优惠权益类型",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "products",
                type: "bigint",
                nullable: false,
                comment: "商品乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "商品 SKU 原始值",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "products",
                type: "bigint",
                nullable: false,
                comment: "向下游发布快照时使用的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "商品当前目录单价",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedSku",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "用于唯一匹配的标准化 SKU",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<int>(
                name: "LifecycleStatus",
                table: "products",
                type: "integer",
                nullable: false,
                comment: "商品生命周期状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "products",
                type: "boolean",
                nullable: false,
                comment: "面向公共目录查询的可售状态投影",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EffectiveOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true,
                comment: "商品发布生效的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "价格的三位币种代码",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "products",
                type: "uuid",
                nullable: false,
                comment: "商品所属分类标识",
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
                table: "coupons",
                type: "bigint",
                nullable: false,
                comment: "优惠券乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ValidUntilUtc",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: false,
                comment: "优惠券有效期结束的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ValidFromUtc",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: false,
                comment: "优惠券有效期开始的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "TotalLimit",
                table: "coupons",
                type: "integer",
                nullable: true,
                comment: "全局可核销总量；为空表示不限制",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "coupons",
                type: "integer",
                nullable: false,
                comment: "优惠券生命周期状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "coupons",
                type: "bigint",
                nullable: false,
                comment: "优惠券定义的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "QuotaStrategy",
                table: "coupons",
                type: "integer",
                nullable: false,
                comment: "券配额的单行或分桶并发策略",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "PromotionId",
                table: "coupons",
                type: "uuid",
                nullable: false,
                comment: "优惠券关联的促销活动标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "PerUserLimit",
                table: "coupons",
                type: "integer",
                nullable: false,
                comment: "单个用户最多可核销次数",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "MaskedCode",
                table: "coupons",
                type: "character varying(124)",
                maxLength: 124,
                nullable: false,
                comment: "用于展示和审计的脱敏券码",
                oldClrType: typeof(string),
                oldType: "character varying(124)",
                oldMaxLength: 124);

            migrationBuilder.AlterColumn<string>(
                name: "CodeDigest",
                table: "coupons",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "标准化券码的 SHA-256 摘要，不保存券码明文",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedUserId",
                table: "coupons",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "专属券绑定的用户标识；为空表示不限定用户",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Capacity",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                comment: "该配额桶可提供的核销数量",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "BucketNumber",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                comment: "用于分散热点写入的配额桶编号",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "categories",
                type: "bigint",
                nullable: false,
                comment: "分类乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "categories",
                type: "integer",
                nullable: false,
                comment: "分类生命周期状态",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                comment: "用于唯一匹配的标准化分类编码",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                comment: "分类业务编码",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "catalog_audit_entries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "业务操作名称",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredOnUtc",
                table: "catalog_audit_entries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "业务操作发生的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "DetailsJson",
                table: "catalog_audit_entries",
                type: "jsonb",
                nullable: true,
                comment: "操作附加详情的 JSON 快照",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AggregateType",
                table: "catalog_audit_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "发生变更的聚合类型",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "AggregateId",
                table: "catalog_audit_entries",
                type: "uuid",
                nullable: false,
                comment: "发生变更的聚合标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Actor",
                table: "catalog_audit_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "执行操作的主体标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
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
                table: "promotions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "促销乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "TriggerType",
                table: "promotions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "促销触发方式：自动或优惠券");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "promotions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "促销生命周期状态");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartsOnUtc",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "促销窗口开始的 UTC 时间");

            migrationBuilder.AlterColumn<int>(
                name: "StackPolicy",
                table: "promotions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "促销是否允许与其他优惠叠加");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "promotions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "促销定义的业务修订号");

            migrationBuilder.AlterColumn<long>(
                name: "PublishedRevision",
                table: "promotions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "当前已发布定义对应的业务修订号");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "promotions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "多促销候选时的计算优先级");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                table: "promotions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldComment: "用于唯一匹配的标准化促销编码");

            migrationBuilder.AlterColumn<string>(
                name: "ExclusiveGroup",
                table: "promotions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true,
                oldComment: "互斥促销分组标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EndsOnUtc",
                table: "promotions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "促销窗口结束的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "DefinitionChecksum",
                table: "promotions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "已发布促销定义的 SHA-256 校验值");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "promotions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldComment: "促销活动业务编码");

            migrationBuilder.AlterColumn<string>(
                name: "TargetType",
                table: "promotion_targets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "促销目标范围类型");

            migrationBuilder.AlterColumn<string>(
                name: "TargetKey",
                table: "promotion_targets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "目标商品、SKU 或分类的业务键");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_targets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "同类组件的稳定计算顺序");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_targets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "组件配置结构版本");

            migrationBuilder.AlterColumn<bool>(
                name: "IsExcluded",
                table: "promotion_targets",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否从促销目标范围中排除");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_targets",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "规则、权益或目标的规范化 JSON 配置");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_rules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "同类组件的稳定计算顺序");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_rules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "组件配置结构版本");

            migrationBuilder.AlterColumn<string>(
                name: "RuleType",
                table: "promotion_rules",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "促销适用条件类型");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_rules",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "规则、权益或目标的规范化 JSON 配置");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "promotion_benefits",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "同类组件的稳定计算顺序");

            migrationBuilder.AlterColumn<int>(
                name: "SchemaVersion",
                table: "promotion_benefits",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "组件配置结构版本");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigurationJson",
                table: "promotion_benefits",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldComment: "规则、权益或目标的规范化 JSON 配置");

            migrationBuilder.AlterColumn<string>(
                name: "BenefitType",
                table: "promotion_benefits",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "促销优惠权益类型");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "products",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "商品乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "商品 SKU 原始值");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "products",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "向下游发布快照时使用的业务修订号");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldComment: "商品当前目录单价");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedSku",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "用于唯一匹配的标准化 SKU");

            migrationBuilder.AlterColumn<int>(
                name: "LifecycleStatus",
                table: "products",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "商品生命周期状态");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "products",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "面向公共目录查询的可售状态投影");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EffectiveOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "商品发布生效的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldComment: "价格的三位币种代码");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "products",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "商品所属分类标识");

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
                table: "coupons",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "优惠券乐观并发版本号");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ValidUntilUtc",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "优惠券有效期结束的 UTC 时间");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ValidFromUtc",
                table: "coupons",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "优惠券有效期开始的 UTC 时间");

            migrationBuilder.AlterColumn<int>(
                name: "TotalLimit",
                table: "coupons",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "全局可核销总量；为空表示不限制");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "coupons",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "优惠券生命周期状态");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "coupons",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "优惠券定义的业务修订号");

            migrationBuilder.AlterColumn<int>(
                name: "QuotaStrategy",
                table: "coupons",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "券配额的单行或分桶并发策略");

            migrationBuilder.AlterColumn<Guid>(
                name: "PromotionId",
                table: "coupons",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "优惠券关联的促销活动标识");

            migrationBuilder.AlterColumn<int>(
                name: "PerUserLimit",
                table: "coupons",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "单个用户最多可核销次数");

            migrationBuilder.AlterColumn<string>(
                name: "MaskedCode",
                table: "coupons",
                type: "character varying(124)",
                maxLength: 124,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(124)",
                oldMaxLength: 124,
                oldComment: "用于展示和审计的脱敏券码");

            migrationBuilder.AlterColumn<string>(
                name: "CodeDigest",
                table: "coupons",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "标准化券码的 SHA-256 摘要，不保存券码明文");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedUserId",
                table: "coupons",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "专属券绑定的用户标识；为空表示不限定用户");

            migrationBuilder.AlterColumn<int>(
                name: "Capacity",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "该配额桶可提供的核销数量");

            migrationBuilder.AlterColumn<int>(
                name: "BucketNumber",
                table: "coupon_quota_buckets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "用于分散热点写入的配额桶编号");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "categories",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "分类乐观并发版本号");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "categories",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "分类生命周期状态");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedCode",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldComment: "用于唯一匹配的标准化分类编码");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldComment: "分类业务编码");

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "catalog_audit_entries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "业务操作名称");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredOnUtc",
                table: "catalog_audit_entries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "业务操作发生的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "DetailsJson",
                table: "catalog_audit_entries",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "操作附加详情的 JSON 快照");

            migrationBuilder.AlterColumn<string>(
                name: "AggregateType",
                table: "catalog_audit_entries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "发生变更的聚合类型");

            migrationBuilder.AlterColumn<Guid>(
                name: "AggregateId",
                table: "catalog_audit_entries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "发生变更的聚合标识");

            migrationBuilder.AlterColumn<string>(
                name: "Actor",
                table: "catalog_audit_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "执行操作的主体标识");
        }
    }
}
