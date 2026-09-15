using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.BasketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_product_snapshots",
                table: "basket_product_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_basket_items_BasketId_ProductId",
                table: "basket_items");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "basket_product_snapshots");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "basket_product_snapshots",
                type: "uuid",
                nullable: false,
                comment: "目录 SPU 商品标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "basket_product_snapshots",
                type: "uuid",
                nullable: false,
                comment: "目录 SKU 变体标识");

            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "basket_product_snapshots",
                type: "jsonb",
                nullable: false,
                comment: "目录 SKU 销售属性的本地投影");

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "basket_product_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "目录 SPU 商品名称的本地投影");

            migrationBuilder.AddColumn<string>(
                name: "VariantName",
                table: "basket_product_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "目录 SKU 展示名称的本地投影");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "basket_items",
                type: "uuid",
                nullable: false,
                comment: "购物项所属的 SPU 商品标识",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "VariantAttributesJsonAtSelection",
                table: "basket_items",
                type: "jsonb",
                nullable: false,
                comment: "最近选择时的 SKU 销售属性快照");

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "basket_items",
                type: "uuid",
                nullable: false,
                comment: "购物项选择的 SKU 变体标识");

            migrationBuilder.AddColumn<string>(
                name: "VariantNameAtSelection",
                table: "basket_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "最近选择时的 SKU 展示名称快照");

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_product_snapshots",
                table: "basket_product_snapshots",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_basket_product_snapshots_ProductId",
                table: "basket_product_snapshots",
                column: "ProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_basket_product_snapshots_attributes",
                table: "basket_product_snapshots",
                sql: "jsonb_typeof(\"AttributesJson\") = 'object'");

            migrationBuilder.CreateIndex(
                name: "IX_basket_items_BasketId_VariantId",
                table: "basket_items",
                columns: new[] { "BasketId", "VariantId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_basket_items_variant_attributes",
                table: "basket_items",
                sql: "jsonb_typeof(\"VariantAttributesJsonAtSelection\") = 'object'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_product_snapshots",
                table: "basket_product_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_basket_product_snapshots_ProductId",
                table: "basket_product_snapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_basket_product_snapshots_attributes",
                table: "basket_product_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_basket_items_BasketId_VariantId",
                table: "basket_items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_basket_items_variant_attributes",
                table: "basket_items");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "basket_product_snapshots");

            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "basket_product_snapshots");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "basket_product_snapshots");

            migrationBuilder.DropColumn(
                name: "VariantName",
                table: "basket_product_snapshots");

            migrationBuilder.DropColumn(
                name: "VariantAttributesJsonAtSelection",
                table: "basket_items");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "basket_items");

            migrationBuilder.DropColumn(
                name: "VariantNameAtSelection",
                table: "basket_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "basket_product_snapshots",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "目录 SPU 商品标识");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "basket_product_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "目录商品名称的本地投影");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "basket_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "购物项所属的 SPU 商品标识");

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_product_snapshots",
                table: "basket_product_snapshots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_basket_items_BasketId_ProductId",
                table: "basket_items",
                columns: new[] { "BasketId", "ProductId" },
                unique: true);
        }
    }
}
