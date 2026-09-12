using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_notifications_payments_PaymentId",
                table: "payment_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_provider_operations_payments_PaymentId",
                table: "payment_provider_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                table: "payment_refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_status_history_payments_PaymentId",
                table: "payment_status_history");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_payments",
                table: "payments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "payment_saga_states");

            migrationBuilder.RenameTable(
                name: "payments",
                newName: "payment_payments");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "payment_outbox_messages");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "payment_inbox_messages");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "payment_saga_states",
                newName: "IX_payment_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilU~");

            migrationBuilder.RenameIndex(
                name: "IX_payments_OrderId",
                table: "payment_payments",
                newName: "IX_payment_payments_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_payments_MerchantTradeNo",
                table: "payment_payments",
                newName: "IX_payment_payments_MerchantTradeNo");

            migrationBuilder.RenameIndex(
                name: "IX_payments_Channel_ProviderTransactionId",
                table: "payment_payments",
                newName: "IX_payment_payments_Channel_ProviderTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "payment_outbox_messages",
                newName: "IX_payment_outbox_messages_Status_NextAttemptOnUtc_LockedUntil~");

            migrationBuilder.AddPrimaryKey(
                name: "PK_payment_saga_states",
                table: "payment_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_payment_payments",
                table: "payment_payments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_payment_outbox_messages",
                table: "payment_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_payment_inbox_messages",
                table: "payment_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_notifications_payment_payments_PaymentId",
                table: "payment_notifications",
                column: "PaymentId",
                principalTable: "payment_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_provider_operations_payment_payments_PaymentId",
                table: "payment_provider_operations",
                column: "PaymentId",
                principalTable: "payment_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_refunds_payment_payments_PaymentId",
                table: "payment_refunds",
                column: "PaymentId",
                principalTable: "payment_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_status_history_payment_payments_PaymentId",
                table: "payment_status_history",
                column: "PaymentId",
                principalTable: "payment_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_notifications_payment_payments_PaymentId",
                table: "payment_notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_provider_operations_payment_payments_PaymentId",
                table: "payment_provider_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_refunds_payment_payments_PaymentId",
                table: "payment_refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_status_history_payment_payments_PaymentId",
                table: "payment_status_history");

            migrationBuilder.DropPrimaryKey(
                name: "PK_payment_saga_states",
                table: "payment_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_payment_payments",
                table: "payment_payments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_payment_outbox_messages",
                table: "payment_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_payment_inbox_messages",
                table: "payment_inbox_messages");

            migrationBuilder.RenameTable(
                name: "payment_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "payment_payments",
                newName: "payments");

            migrationBuilder.RenameTable(
                name: "payment_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "payment_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameIndex(
                name: "IX_payment_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilU~",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_payment_payments_OrderId",
                table: "payments",
                newName: "IX_payments_OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_payment_payments_MerchantTradeNo",
                table: "payments",
                newName: "IX_payments_MerchantTradeNo");

            migrationBuilder.RenameIndex(
                name: "IX_payment_payments_Channel_ProviderTransactionId",
                table: "payments",
                newName: "IX_payments_Channel_ProviderTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_payment_outbox_messages_Status_NextAttemptOnUtc_LockedUntil~",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_payments",
                table: "payments",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_notifications_payments_PaymentId",
                table: "payment_notifications",
                column: "PaymentId",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_provider_operations_payments_PaymentId",
                table: "payment_provider_operations",
                column: "PaymentId",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_refunds_payments_PaymentId",
                table: "payment_refunds",
                column: "PaymentId",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_status_history_payments_PaymentId",
                table: "payment_status_history",
                column: "PaymentId",
                principalTable: "payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
