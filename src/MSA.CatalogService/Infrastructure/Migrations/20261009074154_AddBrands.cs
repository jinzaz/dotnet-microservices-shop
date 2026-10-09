using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BrandId",
                table: "catalog_products",
                type: "uuid",
                nullable: true,
                comment: "商品品牌标识；空值表示无品牌");

            migrationBuilder.CreateTable(
                name: "catalog_brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, comment: "品牌业务编码"),
                    NormalizedCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, comment: "用于唯一匹配的标准化品牌编码"),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "品牌名称"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "品牌生命周期状态"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "品牌乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_brands", x => x.Id);
                    table.CheckConstraint("CK_brands_audit", "\"Version\" > 0 AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_brands_normalized_code", "\"NormalizedCode\" = upper(trim(\"Code\"))");
                    table.CheckConstraint("CK_brands_status", "\"Status\" BETWEEN 0 AND 2");
                },
                comment: "商品品牌");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_products_BrandId_LifecycleStatus_Id",
                table: "catalog_products",
                columns: new[] { "BrandId", "LifecycleStatus", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_brands_NormalizedCode",
                table: "catalog_brands",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_brands_Status_Name_Id",
                table: "catalog_brands",
                columns: new[] { "Status", "Name", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_catalog_brands_BrandId",
                table: "catalog_products",
                column: "BrandId",
                principalTable: "catalog_brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_catalog_brands_BrandId",
                table: "catalog_products");

            migrationBuilder.DropTable(
                name: "catalog_brands");

            migrationBuilder.DropIndex(
                name: "IX_catalog_products_BrandId_LifecycleStatus_Id",
                table: "catalog_products");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "catalog_products");
        }
    }
}
