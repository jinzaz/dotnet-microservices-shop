using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_status_history_status",
                table: "order_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_status",
                table: "order_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_coupon_redemptions_status",
                table: "order_coupon_redemptions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_status_history_FromStatus_Enum",
                table: "order_status_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_status_history_ToStatus_Enum",
                table: "order_status_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_saga_states_Status_Enum",
                table: "order_saga_states",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_outbox_messages_Status_Enum",
                table: "order_outbox_messages",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders",
                sql: "\"FulfillmentStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_orders_Status_Enum",
                table: "order_orders",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_coupon_redemptions_Status_Enum",
                table: "order_coupon_redemptions",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_requests_Reason_Enum",
                table: "order_after_sales_requests",
                sql: "\"Reason\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_requests_ReplacementStock_Enum",
                table: "order_after_sales_requests",
                sql: "\"ReplacementStock\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_requests_ReturnDisposition_Enum",
                table: "order_after_sales_requests",
                sql: "\"ReturnDisposition\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_requests_Status_Enum",
                table: "order_after_sales_requests",
                sql: "\"Status\" BETWEEN 0 AND 11");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_requests_Type_Enum",
                table: "order_after_sales_requests",
                sql: "\"Type\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_history_FromStatus_Enum",
                table: "order_after_sales_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 11");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_after_sales_history_ToStatus_Enum",
                table: "order_after_sales_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 11");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_status_history_FromStatus_Enum",
                table: "order_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_status_history_ToStatus_Enum",
                table: "order_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_saga_states_Status_Enum",
                table: "order_saga_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_outbox_messages_Status_Enum",
                table: "order_outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_orders_FulfillmentStatus_Enum",
                table: "order_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_orders_Status_Enum",
                table: "order_orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_FromStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_fulfillment_status_history_ToStatus_Enum",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_coupon_redemptions_Status_Enum",
                table: "order_coupon_redemptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_requests_Reason_Enum",
                table: "order_after_sales_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_requests_ReplacementStock_Enum",
                table: "order_after_sales_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_requests_ReturnDisposition_Enum",
                table: "order_after_sales_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_requests_Status_Enum",
                table: "order_after_sales_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_requests_Type_Enum",
                table: "order_after_sales_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_history_FromStatus_Enum",
                table: "order_after_sales_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_after_sales_history_ToStatus_Enum",
                table: "order_after_sales_history");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_status_history_status",
                table: "order_status_history",
                sql: "(\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 0 AND 3) AND \"ToStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_status",
                table: "order_orders",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_coupon_redemptions_status",
                table: "order_coupon_redemptions",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }
    }
}
