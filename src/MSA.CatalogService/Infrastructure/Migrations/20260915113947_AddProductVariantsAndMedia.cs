using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVariantsAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_catalog_products_NormalizedSku",
                table: "catalog_products");

            migrationBuilder.DropIndex(
                name: "IX_catalog_products_Sku",
                table: "catalog_products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_currency",
                table: "catalog_products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_normalized_sku",
                table: "catalog_products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_price",
                table: "catalog_products");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "catalog_products");

            migrationBuilder.DropColumn(
                name: "NormalizedSku",
                table: "catalog_products");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "catalog_products");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "catalog_products");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "catalog_products",
                type: "bigint",
                nullable: false,
                comment: "向下游发布商品快照时使用的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "向下游发布快照时使用的业务修订号");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "catalog_products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "SPU 商品名称",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "catalog_products",
                type: "boolean",
                nullable: false,
                comment: "面向公共目录查询的可售状态",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "面向公共目录查询的可售状态投影");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "catalog_products",
                type: "text",
                nullable: false,
                comment: "SPU 商品描述",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "catalog_product_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false, comment: "SKU 所属的 SPU 商品标识"),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "SKU 原始值"),
                    NormalizedSku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "用于唯一匹配的标准化 SKU"),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "SKU 展示名称"),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: false, comment: "颜色、尺码等销售属性 JSON 对象"),
                    Barcode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true, comment: "SKU 条码"),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "SKU 当前目录单价"),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "价格的三位币种代码"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, comment: "SKU 是否启用销售"),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, comment: "是否为兼容单 SKU 接口的默认 SKU"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "SKU 创建的 UTC 时间"),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "SKU 最近更新的 UTC 时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_product_variants", x => x.Id);
                    table.CheckConstraint("CK_product_variants_attributes", "jsonb_typeof(\"AttributesJson\") = 'object'");
                    table.CheckConstraint("CK_product_variants_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_product_variants_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_product_variants_normalized_sku", "\"NormalizedSku\" = upper(trim(\"Sku\"))");
                    table.CheckConstraint("CK_product_variants_price", "\"Price\" >= 0");
                    table.ForeignKey(
                        name: "FK_catalog_product_variants_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalog_product_media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false, comment: "媒体所属的 SPU 商品标识"),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true, comment: "媒体限定的 SKU 标识；空值表示 SPU 通用媒体"),
                    MediaType = table.Column<int>(type: "integer", nullable: false, comment: "媒体类型：图片或视频"),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, comment: "媒体资源 URL"),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, comment: "视频或大图的缩略图 URL"),
                    AltText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "图片替代文本或视频说明"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, comment: "同一商品内的展示顺序"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "媒体创建的 UTC 时间"),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "媒体最近更新的 UTC 时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_product_media", x => x.Id);
                    table.CheckConstraint("CK_product_media_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_product_media_sort_order", "\"SortOrder\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_product_media_type", "\"MediaType\" BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_catalog_product_media_catalog_product_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "catalog_product_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_catalog_product_media_catalog_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "catalog_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_media_ProductId_VariantId_SortOrder_Id",
                table: "catalog_product_media",
                columns: new[] { "ProductId", "VariantId", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_media_VariantId",
                table: "catalog_product_media",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_variants_Barcode",
                table: "catalog_product_variants",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_variants_NormalizedSku",
                table: "catalog_product_variants",
                column: "NormalizedSku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_variants_ProductId_IsActive_Id",
                table: "catalog_product_variants",
                columns: new[] { "ProductId", "IsActive", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_product_variants_ProductId_IsDefault",
                table: "catalog_product_variants",
                columns: new[] { "ProductId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_product_media");

            migrationBuilder.DropTable(
                name: "catalog_product_variants");

            migrationBuilder.AlterColumn<long>(
                name: "Revision",
                table: "catalog_products",
                type: "bigint",
                nullable: false,
                comment: "向下游发布快照时使用的业务修订号",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "向下游发布商品快照时使用的业务修订号");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "catalog_products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "SPU 商品名称");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "catalog_products",
                type: "boolean",
                nullable: false,
                comment: "面向公共目录查询的可售状态投影",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "面向公共目录查询的可售状态");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "catalog_products",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "SPU 商品描述");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "catalog_products",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                comment: "价格的三位币种代码");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedSku",
                table: "catalog_products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "用于唯一匹配的标准化 SKU");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "catalog_products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                comment: "商品当前目录单价");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "catalog_products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "商品 SKU 原始值");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_NormalizedSku",
                table: "catalog_products",
                column: "NormalizedSku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_Sku",
                table: "catalog_products",
                column: "Sku",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_currency",
                table: "catalog_products",
                sql: "\"Currency\" ~ '^[A-Z]{3}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_normalized_sku",
                table: "catalog_products",
                sql: "\"NormalizedSku\" = upper(trim(\"Sku\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_price",
                table: "catalog_products",
                sql: "\"Price\" >= 0");
        }
    }
}
