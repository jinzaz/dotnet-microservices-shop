using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.NotificationService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_user_notification_preferences",
                table: "user_notification_preferences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notifications",
                table: "notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "user_notification_preferences",
                newName: "notification_user_notification_preferences");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "notification_saga_states");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "notification_outbox_messages");

            migrationBuilder.RenameTable(
                name: "notifications",
                newName: "notification_notifications");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "notification_inbox_messages");

            migrationBuilder.RenameIndex(
                name: "IX_user_notification_preferences_UserId",
                table: "notification_user_notification_preferences",
                newName: "IX_notification_user_notification_preferences_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "notification_saga_states",
                newName: "IX_notification_saga_states_Status_TimeoutOnUtc_TimeoutLockedU~");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "notification_outbox_messages",
                newName: "IX_notification_outbox_messages_Status_NextAttemptOnUtc_Locked~");

            migrationBuilder.RenameIndex(
                name: "IX_notifications_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "notification_notifications",
                newName: "IX_notification_notifications_Status_NextAttemptOnUtc_LockedUn~");

            migrationBuilder.RenameIndex(
                name: "IX_notifications_DeduplicationKey",
                table: "notification_notifications",
                newName: "IX_notification_notifications_DeduplicationKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_user_notification_preferences",
                table: "notification_user_notification_preferences",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_saga_states",
                table: "notification_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_outbox_messages",
                table: "notification_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_notifications",
                table: "notification_notifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notification_inbox_messages",
                table: "notification_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_user_notification_preferences",
                table: "notification_user_notification_preferences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_saga_states",
                table: "notification_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_outbox_messages",
                table: "notification_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_notifications",
                table: "notification_notifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_notification_inbox_messages",
                table: "notification_inbox_messages");

            migrationBuilder.RenameTable(
                name: "notification_user_notification_preferences",
                newName: "user_notification_preferences");

            migrationBuilder.RenameTable(
                name: "notification_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "notification_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "notification_notifications",
                newName: "notifications");

            migrationBuilder.RenameTable(
                name: "notification_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameIndex(
                name: "IX_notification_user_notification_preferences_UserId",
                table: "user_notification_preferences",
                newName: "IX_user_notification_preferences_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_notification_saga_states_Status_TimeoutOnUtc_TimeoutLockedU~",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_notification_outbox_messages_Status_NextAttemptOnUtc_Locked~",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_notification_notifications_Status_NextAttemptOnUtc_LockedUn~",
                table: "notifications",
                newName: "IX_notifications_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_notification_notifications_DeduplicationKey",
                table: "notifications",
                newName: "IX_notifications_DeduplicationKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_notification_preferences",
                table: "user_notification_preferences",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_notifications",
                table: "notifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "MessageId", "Consumer" });
        }
    }
}
