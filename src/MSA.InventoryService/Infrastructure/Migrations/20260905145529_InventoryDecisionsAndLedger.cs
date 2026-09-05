using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InventoryDecisionsAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservation_lines_inventory_reservations_Reservat~",
                table: "inventory_reservation_lines");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservation_lines_ReservationId",
                table: "inventory_reservation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_flash_sale_stock_buckets_allocated",
                table: "flash_sale_stock_buckets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommittedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminalReason",
                table: "inventory_reservations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedOnUtc",
                table: "inventory_reservations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM stock_items
                        WHERE length(trim("Sku")) NOT BETWEEN 1 AND 80)
                       OR EXISTS (
                        SELECT 1 FROM inventory_reservation_lines
                        WHERE length(trim("Sku")) NOT BETWEEN 1 AND 80)
                    THEN
                        RAISE EXCEPTION 'Inventory migration found an invalid legacy SKU.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM stock_items
                        GROUP BY upper(trim("Sku"))
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Inventory migration found duplicate stock SKUs after case normalization.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM inventory_reservation_lines
                        WHERE "ReservationId" IS NOT NULL
                        GROUP BY "ReservationId", upper(trim("Sku"))
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Inventory migration found duplicate SKU lines in a legacy reservation.';
                    END IF;

                    IF EXISTS (SELECT 1 FROM inventory_reservations WHERE "Status" NOT BETWEEN 0 AND 2)
                    THEN
                        RAISE EXCEPTION 'Inventory migration found an unknown legacy reservation status.';
                    END IF;
                END
                $migration$;

                DELETE FROM inventory_reservation_lines WHERE "ReservationId" IS NULL;

                UPDATE stock_items SET "Sku" = upper(trim("Sku"));
                UPDATE inventory_reservation_lines SET "Sku" = upper(trim("Sku"));
                UPDATE flash_sale_campaigns SET "Sku" = upper(trim("Sku"));

                UPDATE inventory_reservations
                SET "ExpiresOnUtc" = "CreatedOnUtc" + interval '20 minutes',
                    "UpdatedOnUtc" = "CreatedOnUtc",
                    "CommittedOnUtc" = CASE WHEN "Status" = 1 THEN "CreatedOnUtc" ELSE NULL END,
                    "ReleasedOnUtc" = CASE WHEN "Status" = 2 THEN "CreatedOnUtc" ELSE NULL END,
                    "TerminalReason" = CASE WHEN "Status" = 2 THEN 'Migrated legacy release.' ELSE NULL END;

                ALTER TABLE inventory_reservations ALTER COLUMN "ExpiresOnUtc" DROP DEFAULT;
                ALTER TABLE inventory_reservations ALTER COLUMN "UpdatedOnUtc" DROP DEFAULT;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "inventory_reservation_lines",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_request_decisions",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_request_decisions", x => x.OrderId);
                    table.CheckConstraint("CK_inventory_request_decisions_hash", "char_length(\"RequestHash\") = 64");
                    table.CheckConstraint("CK_inventory_request_decisions_result", "(\"Status\" = 0 AND \"ReservationId\" IS NOT NULL AND \"RejectionReason\" IS NULL) OR (\"Status\" = 1 AND \"ReservationId\" IS NULL AND \"RejectionReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_inventory_request_decisions_status", "\"Status\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_inventory_request_decisions_inventory_reservations_Reservat~",
                        column: x => x.ReservationId,
                        principalTable: "inventory_reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    OnHandDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    OnHandAfter = table.Column<int>(type: "integer", nullable: false),
                    ReservedAfter = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("CK_stock_movements_balances", "\"OnHandAfter\" >= 0 AND \"ReservedAfter\" >= 0 AND \"ReservedAfter\" <= \"OnHandAfter\"");
                    table.CheckConstraint("CK_stock_movements_delta", "\"OnHandDelta\" <> 0 OR \"ReservedDelta\" <> 0");
                    table.CheckConstraint("CK_stock_movements_hash", "\"RequestHash\" IS NULL OR char_length(\"RequestHash\") = 64");
                    table.CheckConstraint("CK_stock_movements_idempotency", "(\"IdempotencyKey\" IS NULL AND \"RequestHash\" IS NULL) OR (\"IdempotencyKey\" IS NOT NULL AND \"RequestHash\" IS NOT NULL AND \"OperatorId\" IS NOT NULL)");
                    table.CheckConstraint("CK_stock_movements_reference", "(\"ReferenceType\" = 0 AND \"ReferenceId\" IS NULL) OR (\"ReferenceType\" <> 0 AND \"ReferenceId\" IS NOT NULL)");
                    table.CheckConstraint("CK_stock_movements_reference_type", "\"ReferenceType\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_stock_movements_type", "\"Type\" BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_items_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "stock_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO stock_movements
                    ("Id", "StockItemId", "Sku", "Type", "OnHandDelta", "ReservedDelta",
                     "OnHandAfter", "ReservedAfter", "ReferenceType", "ReferenceId", "Reason",
                     "OperatorId", "IdempotencyKey", "RequestHash", "OccurredOnUtc")
                SELECT
                    md5('inventory-opening:' || stock."Id"::text)::uuid,
                    stock."Id",
                    stock."Sku",
                    1,
                    stock."OnHand",
                    stock."Reserved",
                    stock."OnHand",
                    stock."Reserved",
                    0,
                    NULL,
                    'Migrated opening balance.',
                    'legacy-migration',
                    NULL,
                    NULL,
                    CURRENT_TIMESTAMP
                FROM stock_items AS stock
                WHERE stock."OnHand" <> 0 OR stock."Reserved" <> 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_items_sku",
                table: "stock_items",
                sql: "\"Sku\" = upper(trim(\"Sku\"))");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_Status_ExpiresOnUtc",
                table: "inventory_reservations",
                columns: new[] { "Status", "ExpiresOnUtc" },
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_Status_UpdatedOnUtc",
                table: "inventory_reservations",
                columns: new[] { "Status", "UpdatedOnUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_status",
                table: "inventory_reservations",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_terminal_state",
                table: "inventory_reservations",
                sql: "(\"Status\" = 0 AND \"CommittedOnUtc\" IS NULL AND \"ReleasedOnUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR (\"Status\" = 1 AND \"CommittedOnUtc\" IS NOT NULL AND \"ReleasedOnUtc\" IS NULL AND \"TerminalReason\" IS NULL) OR (\"Status\" = 2 AND \"CommittedOnUtc\" IS NULL AND \"ReleasedOnUtc\" IS NOT NULL AND \"TerminalReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_timestamps",
                table: "inventory_reservations",
                sql: "\"ExpiresOnUtc\" > \"CreatedOnUtc\" AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservation_lines_ReservationId_Sku",
                table: "inventory_reservation_lines",
                columns: new[] { "ReservationId", "Sku" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_flash_sale_stock_buckets_allocated",
                table: "flash_sale_stock_buckets",
                sql: "\"AllocatedQuantity\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_request_decisions_ReservationId",
                table: "inventory_request_decisions",
                column: "ReservationId",
                unique: true,
                filter: "\"ReservationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_request_decisions_Status_DecidedOnUtc",
                table: "inventory_request_decisions",
                columns: new[] { "Status", "DecidedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_OperatorId_IdempotencyKey",
                table: "stock_movements",
                columns: new[] { "OperatorId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ReferenceType_ReferenceId",
                table: "stock_movements",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_Sku_OccurredOnUtc",
                table: "stock_movements",
                columns: new[] { "Sku", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_StockItemId_OccurredOnUtc",
                table: "stock_movements",
                columns: new[] { "StockItemId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_Type_ReferenceId_StockItemId",
                table: "stock_movements",
                columns: new[] { "Type", "ReferenceId", "StockItemId" },
                unique: true,
                filter: "\"ReferenceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservation_lines_inventory_reservations_Reservat~",
                table: "inventory_reservation_lines",
                column: "ReservationId",
                principalTable: "inventory_reservations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservation_lines_inventory_reservations_Reservat~",
                table: "inventory_reservation_lines");

            migrationBuilder.DropTable(
                name: "inventory_request_decisions");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_items_sku",
                table: "stock_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_Status_ExpiresOnUtc",
                table: "inventory_reservations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_Status_UpdatedOnUtc",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_status",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_terminal_state",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_timestamps",
                table: "inventory_reservations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservation_lines_ReservationId_Sku",
                table: "inventory_reservation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_flash_sale_stock_buckets_allocated",
                table: "flash_sale_stock_buckets");

            migrationBuilder.DropColumn(
                name: "CommittedOnUtc",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "ExpiresOnUtc",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "ReleasedOnUtc",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "TerminalReason",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "inventory_reservations");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReservationId",
                table: "inventory_reservation_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservation_lines_ReservationId",
                table: "inventory_reservation_lines",
                column: "ReservationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_flash_sale_stock_buckets_allocated",
                table: "flash_sale_stock_buckets",
                sql: "\"AllocatedQuantity\" > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservation_lines_inventory_reservations_Reservat~",
                table: "inventory_reservation_lines",
                column: "ReservationId",
                principalTable: "inventory_reservations",
                principalColumn: "Id");
        }
    }
}
