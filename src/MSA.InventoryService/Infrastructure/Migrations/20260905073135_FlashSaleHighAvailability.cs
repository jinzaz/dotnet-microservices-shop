using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FlashSaleHighAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "inventory_reservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "inventory_reservations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "flash_sale_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TotalQuantity = table.Column<int>(type: "integer", nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurchaseLimit = table.Column<int>(type: "integer", nullable: false),
                    Strategy = table.Column<int>(type: "integer", nullable: false),
                    BucketCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flash_sale_campaigns", x => x.Id);
                    table.CheckConstraint("CK_flash_sale_campaigns_bucket_count", "\"BucketCount\" BETWEEN 1 AND 128 AND \"BucketCount\" * \"PurchaseLimit\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_flash_sale_campaigns_purchase_limit", "\"PurchaseLimit\" > 0 AND \"PurchaseLimit\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_flash_sale_campaigns_quantity", "\"TotalQuantity\" > 0");
                    table.CheckConstraint("CK_flash_sale_campaigns_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "flash_sale_stock_buckets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketNumber = table.Column<int>(type: "integer", nullable: false),
                    AllocatedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReservedQuantity = table.Column<int>(type: "integer", nullable: false),
                    SoldQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReleasedQuantity = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flash_sale_stock_buckets", x => x.Id);
                    table.CheckConstraint("CK_flash_sale_stock_buckets_allocated", "\"AllocatedQuantity\" > 0");
                    table.CheckConstraint("CK_flash_sale_stock_buckets_counters", "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"");
                    table.ForeignKey(
                        name: "FK_flash_sale_stock_buckets_flash_sale_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "flash_sale_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_items_quantities",
                table: "stock_items",
                sql: "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_FlashSaleCampaignId_UserId",
                table: "inventory_reservations",
                columns: new[] { "FlashSaleCampaignId", "UserId" },
                unique: true,
                filter: "\"FlashSaleCampaignId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservations_FlashSaleStockBucketId",
                table: "inventory_reservations",
                column: "FlashSaleStockBucketId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_flash_sale_metadata",
                table: "inventory_reservations",
                sql: "(\"FlashSaleCampaignId\" IS NULL AND \"FlashSaleStockBucketId\" IS NULL AND \"UserId\" IS NULL) OR (\"FlashSaleCampaignId\" IS NOT NULL AND \"FlashSaleStockBucketId\" IS NOT NULL AND \"UserId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservation_lines_quantity",
                table: "inventory_reservation_lines",
                sql: "\"Quantity\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_flash_sale_campaigns_Status_EndsOnUtc",
                table: "flash_sale_campaigns",
                columns: new[] { "Status", "EndsOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_flash_sale_stock_buckets_CampaignId_BucketNumber",
                table: "flash_sale_stock_buckets",
                columns: new[] { "CampaignId", "BucketNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_flash_sale_campaigns_FlashSaleCampai~",
                table: "inventory_reservations",
                column: "FlashSaleCampaignId",
                principalTable: "flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_flash_sale_stock_buckets_FlashSaleSt~",
                table: "inventory_reservations",
                column: "FlashSaleStockBucketId",
                principalTable: "flash_sale_stock_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_flash_sale_campaigns_FlashSaleCampai~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_flash_sale_stock_buckets_FlashSaleSt~",
                table: "inventory_reservations");

            migrationBuilder.DropTable(
                name: "flash_sale_stock_buckets");

            migrationBuilder.DropTable(
                name: "flash_sale_campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_items_quantities",
                table: "stock_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_FlashSaleCampaignId_UserId",
                table: "inventory_reservations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_FlashSaleStockBucketId",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_flash_sale_metadata",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservation_lines_quantity",
                table: "inventory_reservation_lines");

            migrationBuilder.DropColumn(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "inventory_reservations");
        }
    }
}
