using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FlashSaleHighAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "flash_sale_campaign_projections",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurchaseLimit = table.Column<int>(type: "integer", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flash_sale_campaign_projections", x => x.CampaignId);
                    table.CheckConstraint("CK_flash_sale_campaign_projections_limit", "\"PurchaseLimit\" > 0");
                    table.CheckConstraint("CK_flash_sale_campaign_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_FlashSaleCampaignId_UserId",
                table: "orders",
                columns: new[] { "FlashSaleCampaignId", "UserId" },
                unique: true,
                filter: "\"FlashSaleCampaignId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_amount",
                table: "orders",
                sql: "\"Amount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_quantity",
                table: "order_lines",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_unit_price",
                table: "order_lines",
                sql: "\"UnitPrice\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_flash_sale_campaign_projections_IsOpen_StartsOnUtc_EndsOnUtc",
                table: "flash_sale_campaign_projections",
                columns: new[] { "IsOpen", "StartsOnUtc", "EndsOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flash_sale_campaign_projections");

            migrationBuilder.DropIndex(
                name: "IX_orders_FlashSaleCampaignId_UserId",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_amount",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_quantity",
                table: "order_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_unit_price",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "FlashSaleCampaignId",
                table: "orders");
        }
    }
}
