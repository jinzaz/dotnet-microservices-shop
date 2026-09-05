using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.PaymentService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReliablePaymentProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_ProviderTransactionId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_Status_ExpiresOnUtc",
                table: "payments");

            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM payments
                        WHERE length(trim("UserId")) NOT BETWEEN 1 AND 200
                           OR length(trim("MerchantTradeNo")) NOT BETWEEN 1 AND 64
                           OR length(COALESCE("ProviderTransactionId", '')) > 128
                           OR length(COALESCE("PaymentUrl", '')) > 4096
                           OR length(COALESCE("FailureReason", '')) > 2000
                           OR trim("Currency") !~ '^[A-Za-z]{3}$')
                    THEN
                        RAISE EXCEPTION 'Payment migration found legacy text beyond the supported provider limits.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM payments WHERE "Amount" <= 0 OR "Channel" NOT IN (0, 1) OR "Status" NOT IN (0, 1, 2))
                    THEN
                        RAISE EXCEPTION 'Payment migration found a legacy payment with an invalid amount, channel, or status.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM payments WHERE "Status" = 1 AND ("ProviderTransactionId" IS NULL OR "PaidOnUtc" IS NULL))
                    THEN
                        RAISE EXCEPTION 'Payment migration found a successful legacy payment without capture identifiers.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM payments WHERE "Status" = 2 AND "FailureReason" IS NULL)
                    THEN
                        RAISE EXCEPTION 'Payment migration found a failed legacy payment without a reason.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM payments
                        WHERE "ProviderTransactionId" IS NOT NULL
                        GROUP BY "Channel", "ProviderTransactionId"
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Payment migration found duplicate provider transaction identifiers.';
                    END IF;
                END
                $migration$;

                UPDATE payments SET "Currency" = upper(trim("Currency"));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentUrl",
                table: "payments",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedOnUtc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotHash",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PricingSnapshotId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                ALTER TABLE payments ALTER COLUMN "RefundedAmount" DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "payment_cancellation_intents",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_cancellation_intents", x => x.OrderId);
                    table.CheckConstraint("CK_payment_cancellation_intents_order", "\"OrderId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "payment_notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    NotificationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderNotificationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MerchantTradeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConflictReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReceivedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_notifications", x => x.Id);
                    table.CheckConstraint("CK_payment_notifications_amount", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_payment_notifications_channel", "\"Channel\" IN (0, 1)");
                    table.CheckConstraint("CK_payment_notifications_conflict", "\"Status\" <> 2 OR \"ConflictReason\" IS NOT NULL");
                    table.CheckConstraint("CK_payment_notifications_currency", "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");
                    table.CheckConstraint("CK_payment_notifications_processed", "\"Status\" = 0 OR \"ProcessedOnUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_payment_notifications_status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_payment_notifications_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_refunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantRefundNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_refunds", x => x.Id);
                    table.CheckConstraint("CK_payment_refunds_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_payment_refunds_completion", "\"Status\" = 0 OR \"CompletedOnUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_payment_refunds_currency", "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");
                    table.CheckConstraint("CK_payment_refunds_failure", "\"Status\" <> 2 OR \"FailureReason\" IS NOT NULL");
                    table.CheckConstraint("CK_payment_refunds_request_id", "\"RefundRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_payment_refunds_status", "\"Status\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_payment_refunds_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_payment_refunds_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_status_history", x => x.Id);
                    table.CheckConstraint("CK_payment_status_history_from_status", "\"FromStatus\" IS NULL OR \"FromStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
                    table.CheckConstraint("CK_payment_status_history_to_status", "\"ToStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
                    table.ForeignKey(
                        name: "FK_payment_status_history_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO payment_status_history
                    ("Id", "PaymentId", "FromStatus", "ToStatus", "ReasonCode", "Details", "OccurredOnUtc")
                SELECT
                    md5('payment-history:' || payment."Id"::text)::uuid,
                    payment."Id",
                    NULL,
                    payment."Status",
                    'LegacyStateImported',
                    'Imported current state by ReliablePaymentProcessing migration.',
                    COALESCE(payment."PaidOnUtc", payment."CreatedOnUtc")
                FROM payments AS payment;
                """);

            migrationBuilder.CreateTable(
                name: "payment_provider_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BusinessReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_provider_operations", x => x.Id);
                    table.CheckConstraint("CK_payment_provider_operations_attempts", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_payment_provider_operations_completion", "\"Status\" <> 2 OR \"CompletedOnUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_payment_provider_operations_lease", "(\"Status\" = 1 AND \"LockId\" IS NOT NULL AND \"LockedUntilUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LockId\" IS NULL AND \"LockedUntilUtc\" IS NULL)");
                    table.CheckConstraint("CK_payment_provider_operations_reference", "(\"Type\" = 2 AND \"BusinessReferenceId\" IS NOT NULL) OR (\"Type\" <> 2 AND \"BusinessReferenceId\" IS NULL)");
                    table.CheckConstraint("CK_payment_provider_operations_status", "\"Status\" IN (0, 1, 2, 3, 4)");
                    table.CheckConstraint("CK_payment_provider_operations_type", "\"Type\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_payment_provider_operations_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_payment_provider_operations_payment_refunds_BusinessReferen~",
                        column: x => x.BusinessReferenceId,
                        principalTable: "payment_refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_provider_operations_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO payment_provider_operations
                    ("Id", "PaymentId", "Type", "IdempotencyKey", "BusinessReferenceId", "Reason",
                     "Status", "AttemptCount", "NextAttemptOnUtc", "LockId", "LockedUntilUtc",
                     "LastError", "CreatedOnUtc", "UpdatedOnUtc", "CompletedOnUtc", "Version")
                SELECT
                    md5('payment-create-operation:' || payment."Id"::text)::uuid,
                    payment."Id",
                    0,
                    'create:' || payment."MerchantTradeNo",
                    NULL,
                    NULL,
                    0,
                    0,
                    CURRENT_TIMESTAMP,
                    NULL,
                    NULL,
                    NULL,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP,
                    NULL,
                    0
                FROM payments AS payment
                WHERE payment."Status" = 0
                  AND payment."PaymentUrl" IS NULL
                  AND payment."ExpiresOnUtc" > CURRENT_TIMESTAMP;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_payments_Channel_ProviderTransactionId",
                table: "payments",
                columns: new[] { "Channel", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payments_pending_expiry",
                table: "payments",
                columns: new[] { "ExpiresOnUtc", "Id" },
                filter: "\"Status\" = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_amount",
                table: "payments",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_channel",
                table: "payments",
                sql: "\"Channel\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_closed_fields",
                table: "payments",
                sql: "\"Status\" <> 5 OR \"ClosedOnUtc\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_currency",
                table: "payments",
                sql: "char_length(\"Currency\") = 3 AND \"Currency\" = upper(\"Currency\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_failure_fields",
                table: "payments",
                sql: "\"Status\" NOT IN (2, 6) OR \"FailureReason\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_pricing_snapshot",
                table: "payments",
                sql: "(\"PricingSnapshotId\" IS NULL AND \"PricingSnapshotHash\" IS NULL) OR (\"PricingSnapshotId\" IS NOT NULL AND \"PricingSnapshotHash\" ~ '^[0-9A-F]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_refunded_amount",
                table: "payments",
                sql: "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_refunded_status",
                table: "payments",
                sql: "\"Status\" <> 4 OR \"RefundedAmount\" = \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_status",
                table: "payments",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_succeeded_fields",
                table: "payments",
                sql: "\"Status\" NOT IN (1, 3, 4) OR (\"ProviderTransactionId\" IS NOT NULL AND \"PaidOnUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_version",
                table: "payments",
                sql: "\"Version\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_payment_cancellation_intents_RequestedOnUtc",
                table: "payment_cancellation_intents",
                column: "RequestedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_cancellation_intents_RequestMessageId",
                table: "payment_cancellation_intents",
                column: "RequestMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_notifications_Channel_NotificationKey",
                table: "payment_notifications",
                columns: new[] { "Channel", "NotificationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_notifications_Channel_ProviderNotificationId",
                table: "payment_notifications",
                columns: new[] { "Channel", "ProviderNotificationId" },
                unique: true,
                filter: "\"ProviderNotificationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_notifications_PaymentId_ReceivedOnUtc",
                table: "payment_notifications",
                columns: new[] { "PaymentId", "ReceivedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_operations_BusinessReferenceId",
                table: "payment_provider_operations",
                column: "BusinessReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_operations_expired_lease",
                table: "payment_provider_operations",
                columns: new[] { "LockedUntilUtc", "Id" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_operations_IdempotencyKey",
                table: "payment_provider_operations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_operations_pending",
                table: "payment_provider_operations",
                columns: new[] { "NextAttemptOnUtc", "CreatedOnUtc", "Id" },
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_payment_provider_operations_singleton",
                table: "payment_provider_operations",
                columns: new[] { "PaymentId", "Type" },
                unique: true,
                filter: "\"Type\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_MerchantRefundNo",
                table: "payment_refunds",
                column: "MerchantRefundNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_one_pending_per_payment",
                table: "payment_refunds",
                column: "PaymentId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_PaymentId_Status_CreatedOnUtc",
                table: "payment_refunds",
                columns: new[] { "PaymentId", "Status", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_RefundRequestId",
                table: "payment_refunds",
                column: "RefundRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_RequestMessageId",
                table: "payment_refunds",
                column: "RequestMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_status_history_PaymentId_OccurredOnUtc",
                table: "payment_status_history",
                columns: new[] { "PaymentId", "OccurredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_cancellation_intents");

            migrationBuilder.DropTable(
                name: "payment_notifications");

            migrationBuilder.DropTable(
                name: "payment_provider_operations");

            migrationBuilder.DropTable(
                name: "payment_status_history");

            migrationBuilder.DropTable(
                name: "payment_refunds");

            migrationBuilder.DropIndex(
                name: "IX_payments_Channel_ProviderTransactionId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_pending_expiry",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_amount",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_channel",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_closed_fields",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_currency",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_failure_fields",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_pricing_snapshot",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_refunded_amount",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_refunded_status",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_status",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_succeeded_fields",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_version",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ClosedOnUtc",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotHash",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "payments");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "payments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderTransactionId",
                table: "payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentUrl",
                table: "payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MerchantTradeNo",
                table: "payments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "payments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_ProviderTransactionId",
                table: "payments",
                column: "ProviderTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_Status_ExpiresOnUtc",
                table: "payments",
                columns: new[] { "Status", "ExpiresOnUtc" });
        }
    }
}
