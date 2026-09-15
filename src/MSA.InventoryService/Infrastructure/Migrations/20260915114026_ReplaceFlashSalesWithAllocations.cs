using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFlashSalesWithAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_campaigns_Flash~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_stock_buckets_F~",
                table: "inventory_reservations");

            migrationBuilder.DropTable(
                name: "inventory_flash_sale_stock_buckets");

            migrationBuilder.DropTable(
                name: "inventory_flash_sale_campaigns");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_FlashSaleCampaignId_UserId",
                table: "inventory_reservations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservations_FlashSaleStockBucketId",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_flash_sale_metadata",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations");

            migrationBuilder.DropColumn(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "inventory_reservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "预占所属用户；历史普通预占可能为空",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "秒杀预占所属用户；普通预占为空");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationId",
                table: "inventory_reservation_lines",
                type: "uuid",
                nullable: true,
                comment: "该订单行使用的库存配额标识；普通库存为空");

            migrationBuilder.AddColumn<Guid>(
                name: "OfferId",
                table: "inventory_reservation_lines",
                type: "uuid",
                nullable: true,
                comment: "该订单行选择的促销或限量销售标识；普通库存为空");

            migrationBuilder.CreateTable(
                name: "inventory_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false, comment: "目录促销或限量销售标识"),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "配额对应的标准化 SKU"),
                    TotalQuantity = table.Column<int>(type: "integer", nullable: false, comment: "从常规库存圈定的总数量"),
                    ReservedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "已被未支付订单预占的数量"),
                    SoldQuantity = table.Column<int>(type: "integer", nullable: false, comment: "已支付并正式扣减的数量"),
                    ReleasedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "关闭后已归还常规库存的数量"),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false, comment: "单用户可同时预占及累计售出的数量上限"),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "配额生效的 UTC 时间"),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "配额失效的 UTC 时间"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "配额状态：开放或关闭"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "配额创建的 UTC 时间"),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "配额关闭的 UTC 时间"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "配额乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_allocations", x => x.Id);
                    table.CheckConstraint("CK_inventory_allocations_counters", "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_inventory_allocations_limit", "\"PerUserLimit\" > 0 AND \"PerUserLimit\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_inventory_allocations_quantity", "\"TotalQuantity\" > 0");
                    table.CheckConstraint("CK_inventory_allocations_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "inventory_allocation_buyers",
                columns: table => new
                {
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false, comment: "库存配额标识"),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "统一认证平台用户标识"),
                    ReservedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "该用户当前预占数量"),
                    SoldQuantity = table.Column<int>(type: "integer", nullable: false, comment: "该用户累计售出数量"),
                    ReleasedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "该用户累计释放数量"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "用户计数创建的 UTC 时间"),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "用户计数更新的 UTC 时间"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "用户配额计数乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_allocation_buyers", x => new { x.AllocationId, x.UserId });
                    table.CheckConstraint("CK_inventory_allocation_buyers_counters", "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_allocation_buyers_inventory_allocations_Allocatio~",
                        column: x => x.AllocationId,
                        principalTable: "inventory_allocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_reservation_lines_AllocationId_OfferId",
                table: "inventory_reservation_lines",
                columns: new[] { "AllocationId", "OfferId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservation_lines_allocation",
                table: "inventory_reservation_lines",
                sql: "(\"AllocationId\" IS NULL AND \"OfferId\" IS NULL) OR (\"AllocationId\" IS NOT NULL AND \"OfferId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_OfferId_Sku",
                table: "inventory_allocations",
                columns: new[] { "OfferId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_Status_EndsOnUtc",
                table: "inventory_allocations",
                columns: new[] { "Status", "EndsOnUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservation_lines_inventory_allocations_Allocatio~",
                table: "inventory_reservation_lines",
                column: "AllocationId",
                principalTable: "inventory_allocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservation_lines_inventory_allocations_Allocatio~",
                table: "inventory_reservation_lines");

            migrationBuilder.DropTable(
                name: "inventory_allocation_buyers");

            migrationBuilder.DropTable(
                name: "inventory_allocations");

            migrationBuilder.DropIndex(
                name: "IX_inventory_reservation_lines_AllocationId_OfferId",
                table: "inventory_reservation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservation_lines_allocation",
                table: "inventory_reservation_lines");

            migrationBuilder.DropColumn(
                name: "AllocationId",
                table: "inventory_reservation_lines");

            migrationBuilder.DropColumn(
                name: "OfferId",
                table: "inventory_reservation_lines");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "inventory_reservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "秒杀预占所属用户；普通预占为空",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "预占所属用户；历史普通预占可能为空");

            migrationBuilder.AddColumn<Guid>(
                name: "FlashSaleCampaignId",
                table: "inventory_reservations",
                type: "uuid",
                nullable: true,
                comment: "秒杀活动标识；普通预占为空");

            migrationBuilder.AddColumn<long>(
                name: "FlashSaleStockBucketId",
                table: "inventory_reservations",
                type: "bigint",
                nullable: true,
                comment: "秒杀库存桶标识；普通预占为空");

            migrationBuilder.CreateTable(
                name: "inventory_flash_sale_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketCount = table.Column<int>(type: "integer", nullable: false, comment: "用于分散热点写入的库存桶数量"),
                    ClosedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "活动结束的 UTC 时间"),
                    PurchaseLimit = table.Column<int>(type: "integer", nullable: false, comment: "每个用户在本活动中的购买上限"),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "秒杀商品的标准化 SKU"),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "活动开始的 UTC 时间"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "秒杀活动状态"),
                    Strategy = table.Column<int>(type: "integer", nullable: false, comment: "秒杀库存采用单行或分桶策略"),
                    TotalQuantity = table.Column<int>(type: "integer", nullable: false, comment: "活动分配的库存总量"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "秒杀活动乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_flash_sale_campaigns", x => x.Id);
                    table.CheckConstraint("CK_flash_sale_campaigns_bucket_count", "\"BucketCount\" BETWEEN 1 AND 128 AND \"BucketCount\" * \"PurchaseLimit\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_flash_sale_campaigns_purchase_limit", "\"PurchaseLimit\" > 0 AND \"PurchaseLimit\" <= \"TotalQuantity\"");
                    table.CheckConstraint("CK_flash_sale_campaigns_quantity", "\"TotalQuantity\" > 0");
                    table.CheckConstraint("CK_flash_sale_campaigns_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "inventory_flash_sale_stock_buckets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllocatedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "分配给该桶的库存总量"),
                    BucketNumber = table.Column<int>(type: "integer", nullable: false, comment: "活动内的库存桶编号"),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleasedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "活动关闭后不再销售的库存量"),
                    ReservedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "该桶已预占的库存量"),
                    SoldQuantity = table.Column<int>(type: "integer", nullable: false, comment: "该桶已确认售出的库存量"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "库存桶乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_flash_sale_stock_buckets", x => x.Id);
                    table.CheckConstraint("CK_flash_sale_stock_buckets_allocated", "\"AllocatedQuantity\" >= 0");
                    table.CheckConstraint("CK_flash_sale_stock_buckets_counters", "\"ReservedQuantity\" >= 0 AND \"SoldQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ReservedQuantity\" + \"SoldQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\"");
                    table.ForeignKey(
                        name: "FK_inventory_flash_sale_stock_buckets_inventory_flash_sale_cam~",
                        column: x => x.CampaignId,
                        principalTable: "inventory_flash_sale_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_inventory_flash_sale_campaigns_Status_EndsOnUtc",
                table: "inventory_flash_sale_campaigns",
                columns: new[] { "Status", "EndsOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_flash_sale_stock_buckets_CampaignId_BucketNumber",
                table: "inventory_flash_sale_stock_buckets",
                columns: new[] { "CampaignId", "BucketNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_campaigns_Flash~",
                table: "inventory_reservations",
                column: "FlashSaleCampaignId",
                principalTable: "inventory_flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_stock_buckets_F~",
                table: "inventory_reservations",
                column: "FlashSaleStockBucketId",
                principalTable: "inventory_flash_sale_stock_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
