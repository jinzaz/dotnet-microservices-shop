using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_saga_states_Status_Enum",
                table: "notification_saga_states",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_outbox_messages_Status_Enum",
                table: "notification_outbox_messages",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_notifications_Status_Enum",
                table: "notification_notifications",
                sql: "\"Status\" BETWEEN 0 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_saga_states_Status_Enum",
                table: "notification_saga_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_outbox_messages_Status_Enum",
                table: "notification_outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_notifications_Status_Enum",
                table: "notification_notifications");
        }
    }
}
