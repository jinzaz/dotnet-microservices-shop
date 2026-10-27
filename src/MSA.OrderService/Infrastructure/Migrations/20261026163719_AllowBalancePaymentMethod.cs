using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowBalancePaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_payment_method",
                table: "order_orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_payment_method",
                table: "order_orders",
                sql: "\"PaymentMethod\" IN ('Alipay', 'WeChatPay', 'Balance', 'Unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_payment_method",
                table: "order_orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_payment_method",
                table: "order_orders",
                sql: "\"PaymentMethod\" IN ('Alipay', 'WeChatPay', 'Unknown')");
        }
    }
}
