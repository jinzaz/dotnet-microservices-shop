using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AfterSalesFulfillmentSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.AddColumn<bool>(
                name: "InTransitAtSubmission",
                table: "order_after_sales_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "申请时包裹已发出未签收；此类仅退款不自动同意");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders",
                sql: "\"FulfillmentStatus\" BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropColumn(
                name: "InTransitAtSubmission",
                table: "order_after_sales_requests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders",
                sql: "\"FulfillmentStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 5");
        }
    }
}
