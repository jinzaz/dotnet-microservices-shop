using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_status_history_from_status",
                table: "payment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_status_history_to_status",
                table: "payment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_refunds_status",
                table: "payment_refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_provider_operations_status",
                table: "payment_provider_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_provider_operations_type",
                table: "payment_provider_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_channel",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_status",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_channel",
                table: "payment_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_status",
                table: "payment_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_status_history_FromStatus_Enum",
                table: "payment_status_history",
                sql: "\"FromStatus\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_status_history_ToStatus_Enum",
                table: "payment_status_history",
                sql: "\"ToStatus\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_saga_states_Status_Enum",
                table: "payment_saga_states",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_refunds_Status_Enum",
                table: "payment_refunds",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_provider_operations_Status_Enum",
                table: "payment_provider_operations",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_provider_operations_Type_Enum",
                table: "payment_provider_operations",
                sql: "\"Type\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_payments_Status_Enum",
                table: "payment_payments",
                sql: "\"Status\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_outbox_messages_Status_Enum",
                table: "payment_outbox_messages",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_Status_Enum",
                table: "payment_notifications",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_status_history_FromStatus_Enum",
                table: "payment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_status_history_ToStatus_Enum",
                table: "payment_status_history");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_saga_states_Status_Enum",
                table: "payment_saga_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_refunds_Status_Enum",
                table: "payment_refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_provider_operations_Status_Enum",
                table: "payment_provider_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_provider_operations_Type_Enum",
                table: "payment_provider_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_payments_Channel_Enum",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_payments_Status_Enum",
                table: "payment_payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_outbox_messages_Status_Enum",
                table: "payment_outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_Channel_Enum",
                table: "payment_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payment_notifications_Status_Enum",
                table: "payment_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_status_history_from_status",
                table: "payment_status_history",
                sql: "\"FromStatus\" IS NULL OR \"FromStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_status_history_to_status",
                table: "payment_status_history",
                sql: "\"ToStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_refunds_status",
                table: "payment_refunds",
                sql: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_provider_operations_status",
                table: "payment_provider_operations",
                sql: "\"Status\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_provider_operations_type",
                table: "payment_provider_operations",
                sql: "\"Type\" IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_channel",
                table: "payment_payments",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_status",
                table: "payment_payments",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_channel",
                table: "payment_notifications",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payment_notifications_status",
                table: "payment_notifications",
                sql: "\"Status\" IN (0, 1, 2)");
        }
    }
}
