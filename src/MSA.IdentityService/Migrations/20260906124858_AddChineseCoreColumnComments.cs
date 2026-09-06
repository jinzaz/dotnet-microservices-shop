using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddChineseCoreColumnComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "user_addresses",
                type: "bigint",
                nullable: false,
                comment: "地址乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "user_addresses",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                comment: "地址所属用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "user_addresses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "用户自定义地址标签",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "user_addresses",
                type: "boolean",
                nullable: false,
                comment: "地址是否可用于新订单",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDefault",
                table: "user_addresses",
                type: "boolean",
                nullable: false,
                comment: "是否为用户当前默认有效地址",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedOnUtc",
                table: "user_addresses",
                type: "timestamp with time zone",
                nullable: true,
                comment: "地址软删除的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "user_addresses",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                comment: "ISO 3166-1 alpha-2 国家或地区代码",
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "loyalty_point_entries",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                comment: "积分流水所属用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "loyalty_point_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "积分变动来源类型",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "loyalty_point_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "积分变动原因",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "OperatorId",
                table: "loyalty_point_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "发起积分变动的主体标识",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredOnUtc",
                table: "loyalty_point_entries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "积分变动发生的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "Delta",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                comment: "本次积分变动量，正数为增加、负数为扣减",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "BusinessKey",
                table: "loyalty_point_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "积分业务幂等键",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<long>(
                name: "BalanceBefore",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                comment: "积分变动前余额",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "BalanceAfter",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                comment: "积分变动后余额",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                comment: "积分账户乐观并发版本号",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "LifetimeSpent",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                comment: "账户历史累计消费积分",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "LifetimeEarned",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                comment: "账户历史累计获得积分",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "Balance",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                comment: "当前可用积分余额",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "loyalty_point_accounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                comment: "积分账户所属用户标识",
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastLoginOnUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true,
                comment: "最近一次成功登录的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "账号是否允许登录和执行业务操作",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "用户对外展示名称",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedOnUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                comment: "账号创建的 UTC 时间",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "user_addresses",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "地址乐观并发版本号");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "user_addresses",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldComment: "地址所属用户标识");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "user_addresses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "用户自定义地址标签");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "user_addresses",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "地址是否可用于新订单");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDefault",
                table: "user_addresses",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否为用户当前默认有效地址");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedOnUtc",
                table: "user_addresses",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "地址软删除的 UTC 时间");

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "user_addresses",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(2)",
                oldFixedLength: true,
                oldMaxLength: 2,
                oldComment: "ISO 3166-1 alpha-2 国家或地区代码");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "loyalty_point_entries",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldComment: "积分流水所属用户标识");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "loyalty_point_entries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "积分变动来源类型");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "loyalty_point_entries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "积分变动原因");

            migrationBuilder.AlterColumn<string>(
                name: "OperatorId",
                table: "loyalty_point_entries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "发起积分变动的主体标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "OccurredOnUtc",
                table: "loyalty_point_entries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "积分变动发生的 UTC 时间");

            migrationBuilder.AlterColumn<long>(
                name: "Delta",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "本次积分变动量，正数为增加、负数为扣减");

            migrationBuilder.AlterColumn<string>(
                name: "BusinessKey",
                table: "loyalty_point_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "积分业务幂等键");

            migrationBuilder.AlterColumn<long>(
                name: "BalanceBefore",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "积分变动前余额");

            migrationBuilder.AlterColumn<long>(
                name: "BalanceAfter",
                table: "loyalty_point_entries",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "积分变动后余额");

            migrationBuilder.AlterColumn<long>(
                name: "Version",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "积分账户乐观并发版本号");

            migrationBuilder.AlterColumn<long>(
                name: "LifetimeSpent",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "账户历史累计消费积分");

            migrationBuilder.AlterColumn<long>(
                name: "LifetimeEarned",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "账户历史累计获得积分");

            migrationBuilder.AlterColumn<long>(
                name: "Balance",
                table: "loyalty_point_accounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "当前可用积分余额");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "loyalty_point_accounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldComment: "积分账户所属用户标识");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastLoginOnUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "最近一次成功登录的 UTC 时间");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "账号是否允许登录和执行业务操作");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户对外展示名称");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedOnUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "账号创建的 UTC 时间");
        }
    }
}
