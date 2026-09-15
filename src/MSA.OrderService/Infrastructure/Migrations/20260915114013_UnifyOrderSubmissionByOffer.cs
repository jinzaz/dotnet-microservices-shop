using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyOrderSubmissionByOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_flash_sale_campaign_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections");

            migrationBuilder.DropIndex(
                name: "IX_order_orders_FlashSaleCampaignId_Status",
                table: "order_orders");

            migrationBuilder.DropIndex(
                name: "IX_order_orders_FlashSaleCampaignId_UserId",
                table: "order_orders");

            migrationBuilder.DropColumn(
                name: "FlashSaleCampaignId",
                table: "order_orders");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "order_product_projections",
                newName: "VariantName");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "order_product_projections",
                type: "uuid",
                nullable: false,
                comment: "目录 SPU 商品标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "order_product_projections",
                type: "uuid",
                nullable: false,
                comment: "目录 SKU 变体标识");

            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "order_product_projections",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "order_product_projections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false);

            migrationBuilder.AlterColumn<string>(
                name: "ProductName",
                table: "order_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "下单时的 SPU 商品名称快照",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<Guid>(
                name: "OfferId",
                table: "order_lines",
                type: "uuid",
                nullable: true,
                comment: "该订单行选择的限量销售或促销标识；普通购买为空");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "order_lines",
                type: "uuid",
                nullable: true,
                comment: "下单时的 SPU 商品标识快照");

            migrationBuilder.AddColumn<string>(
                name: "VariantAttributesJson",
                table: "order_lines",
                type: "jsonb",
                nullable: false,
                comment: "下单时的 SKU 销售属性快照");

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "order_lines",
                type: "uuid",
                nullable: true,
                comment: "下单时的 SKU 变体标识快照");

            migrationBuilder.AddColumn<string>(
                name: "VariantName",
                table: "order_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "下单时的 SKU 展示名称快照");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections",
                column: "VariantId");

            migrationBuilder.CreateTable(
                name: "order_inventory_allocation_projections",
                columns: table => new
                {
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false, comment: "库存配额内部标识"),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false, comment: "目录促销或限量销售标识"),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false, comment: "单个用户可同时预占及累计售出的数量上限"),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_inventory_allocation_projections", x => x.AllocationId);
                    table.CheckConstraint("CK_inventory_allocation_projections_limit", "\"PerUserLimit\" > 0");
                    table.CheckConstraint("CK_inventory_allocation_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_product_projections_ProductId",
                table: "order_product_projections",
                column: "ProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_projections_attributes",
                table: "order_product_projections",
                sql: "jsonb_typeof(\"AttributesJson\") = 'object'");

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_OfferId_Sku",
                table: "order_lines",
                columns: new[] { "OfferId", "Sku" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_variant_attributes",
                table: "order_lines",
                sql: "jsonb_typeof(\"VariantAttributesJson\") = 'object'");

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_allocation_projections_IsOpen_StartsOnUtc_E~",
                table: "order_inventory_allocation_projections",
                columns: new[] { "IsOpen", "StartsOnUtc", "EndsOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_inventory_allocation_projections_OfferId_Sku",
                table: "order_inventory_allocation_projections",
                columns: new[] { "OfferId", "Sku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_inventory_allocation_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections");

            migrationBuilder.DropIndex(
                name: "IX_order_product_projections_ProductId",
                table: "order_product_projections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_product_projections_attributes",
                table: "order_product_projections");

            migrationBuilder.DropIndex(
                name: "IX_order_lines_OfferId_Sku",
                table: "order_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_variant_attributes",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "order_product_projections");

            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "order_product_projections");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "order_product_projections");

            migrationBuilder.DropColumn(
                name: "OfferId",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "VariantAttributesJson",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "VariantName",
                table: "order_lines");

            migrationBuilder.RenameColumn(
                name: "VariantName",
                table: "order_product_projections",
                newName: "Name");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "order_product_projections",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "目录 SPU 商品标识");

            migrationBuilder.AddColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "order_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProductName",
                table: "order_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "下单时的 SPU 商品名称快照");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections",
                column: "ProductId");

            migrationBuilder.CreateTable(
                name: "order_flash_sale_campaign_projections",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    PurchaseLimit = table.Column<int>(type: "integer", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_flash_sale_campaign_projections", x => x.CampaignId);
                    table.CheckConstraint("CK_flash_sale_campaign_projections_limit", "\"PurchaseLimit\" > 0");
                    table.CheckConstraint("CK_flash_sale_campaign_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_orders_FlashSaleCampaignId_Status",
                table: "order_orders",
                columns: new[] { "FlashSaleCampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_orders_FlashSaleCampaignId_UserId",
                table: "order_orders",
                columns: new[] { "FlashSaleCampaignId", "UserId" },
                unique: true,
                filter: "\"FlashSaleCampaignId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_flash_sale_campaign_projections_IsOpen_StartsOnUtc_En~",
                table: "order_flash_sale_campaign_projections",
                columns: new[] { "IsOpen", "StartsOnUtc", "EndsOnUtc" });
        }
    }
}
