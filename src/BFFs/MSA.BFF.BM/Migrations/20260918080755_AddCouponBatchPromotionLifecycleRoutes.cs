using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponBatchPromotionLifecycleRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("0161e319-ebf9-aaae-2327-e006d9bb1e29"), "POST", 66, "/api/catalog/admin/promotions/{id}/archive", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("7128c68f-e122-2710-ef4d-02722023bf6c"), "GET", 68, "/api/order/admin/coupons/usage", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("c39467f9-88e0-a33d-4225-afee0516fe7f"), "POST", 67, "/api/catalog/admin/coupons/batch", new Guid("88893bde-9223-0a9a-4835-b9d5a55b0bb5") },
                    { new Guid("ce01652c-e37c-60ba-2046-fbb997488bb7"), "POST", 65, "/api/catalog/admin/promotions/{id}/revise", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("0161e319-ebf9-aaae-2327-e006d9bb1e29"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("7128c68f-e122-2710-ef4d-02722023bf6c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c39467f9-88e0-a33d-4225-afee0516fe7f"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("ce01652c-e37c-60ba-2046-fbb997488bb7"));
        }
    }
}
