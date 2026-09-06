using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingAddressAndFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierName",
                table: "orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentStatus",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InTransitOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippedOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingAddressId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCity",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingCountryCode",
                table: "orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingDistrict",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingPhoneNumber",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingPostalCode",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingProvince",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingRecipientName",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingStreetLine1",
                table: "orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingStreetLine2",
                table: "orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_fulfillment_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CarrierName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_fulfillment_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_fulfillment_status_history_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                UPDATE orders
                SET "FulfillmentStatus" = CASE
                        WHEN "Status" = 2 THEN 1
                        WHEN "Status" = 3 THEN 5
                        ELSE 0
                    END,
                    "ShippingRecipientName" = 'Legacy recipient',
                    "ShippingPhoneNumber" = 'UNKNOWN',
                    "ShippingCountryCode" = 'ZZ',
                    "ShippingProvince" = 'UNKNOWN',
                    "ShippingCity" = 'UNKNOWN',
                    "ShippingDistrict" = 'UNKNOWN',
                    "ShippingStreetLine1" = 'UNKNOWN';

                INSERT INTO order_fulfillment_status_history
                    ("Id", "OrderId", "FromStatus", "ToStatus", "CarrierCode", "CarrierName",
                     "TrackingNumber", "Note", "Actor", "IdempotencyKey", "RequestFingerprint", "OccurredOnUtc")
                SELECT
                    md5('order-fulfillment-history:' || ordering."Id"::text)::uuid,
                    ordering."Id",
                    NULL,
                    ordering."FulfillmentStatus",
                    NULL,
                    NULL,
                    NULL,
                    'Initial fulfillment state created while migrating an existing order.',
                    'migration',
                    NULL,
                    NULL,
                    COALESCE(ordering."ConfirmedOnUtc", ordering."CancelledOnUtc", ordering."CreatedOnUtc")
                FROM orders AS ordering;

                ALTER TABLE orders ALTER COLUMN "FulfillmentStatus" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingCity" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingCountryCode" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingDistrict" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingPhoneNumber" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingProvince" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingRecipientName" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "ShippingStreetLine1" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_orders_FulfillmentStatus_UpdatedOnUtc",
                table: "orders",
                columns: new[] { "FulfillmentStatus", "UpdatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_fulfillment_status_history_OrderId_IdempotencyKey",
                table: "order_fulfillment_status_history",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_fulfillment_status_history_OrderId_OccurredOnUtc",
                table: "order_fulfillment_status_history",
                columns: new[] { "OrderId", "OccurredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_fulfillment_status_history");

            migrationBuilder.DropIndex(
                name: "IX_orders_FulfillmentStatus_UpdatedOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CarrierName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveredOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "InTransitOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippedOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingAddressId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingCity",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingCountryCode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingDistrict",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingPhoneNumber",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingPostalCode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingProvince",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingRecipientName",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingStreetLine1",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippingStreetLine2",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "orders");
        }
    }
}
