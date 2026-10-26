using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications");

            migrationBuilder.CreateTable(
                name: "payment_balance_accounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "余额所属用户标识"),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "当前可用余额（CNY），不能为负"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "余额账户乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_balance_accounts", x => x.Id);
                    table.CheckConstraint("CK_payment_balance_accounts_balance", "\"Balance\" >= 0");
                    table.CheckConstraint("CK_payment_balance_accounts_version", "\"Version\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "payment_balance_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "余额所属用户标识"),
                    Type = table.Column<int>(type: "integer", nullable: false, comment: "流水类型：后台调整、支付扣款或退款入账"),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "余额变动金额，扣减为负"),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "本次变动后的余额"),
                    BusinessKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "记账幂等键"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true, comment: "关联订单标识"),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true, comment: "关联支付单标识"),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: true, comment: "关联退款单标识"),
                    AfterSalesRequestNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "关联售后单号"),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "变动原因"),
                    OperatorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "后台调整的操作人标识"),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_balance_entries", x => x.Id);
                    table.CheckConstraint("CK_payment_balance_entries_amount", "\"Amount\" <> 0");
                    table.CheckConstraint("CK_payment_balance_entries_balance_after", "\"BalanceAfter\" >= 0");
                    table.CheckConstraint("CK_payment_balance_entries_direction", "(\"Type\" <> 1 OR \"Amount\" < 0) AND (\"Type\" <> 2 OR \"Amount\" > 0)");
                    table.CheckConstraint("CK_payment_balance_entries_Type_Enum", "\"Type\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_payment_balance_entries_payment_balance_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "payment_balance_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments",
                sql: "\"Channel\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications",
                sql: "\"Channel\" BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_payment_balance_entries_UserId_BusinessKey",
                table: "payment_balance_entries",
                columns: new[] { "UserId", "BusinessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_balance_entries_UserId_OccurredOnUtc",
                table: "payment_balance_entries",
                columns: new[] { "UserId", "OccurredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_balance_entries");

            migrationBuilder.DropTable(
                name: "payment_balance_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications",
                sql: "\"Channel\" IN (0, 1)");
        }
    }
}
