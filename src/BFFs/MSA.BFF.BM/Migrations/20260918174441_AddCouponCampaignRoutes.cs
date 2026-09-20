using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponCampaignRoutes : Migration
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
                    { new Guid("0a6f9522-dc68-b314-9196-284de181f461"), "POST", 78, "/api/catalog/admin/coupon-campaigns/{id}/end", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("433a307b-bef0-83cc-c6a5-66ea1b3601c7"), "POST", 77, "/api/catalog/admin/coupon-campaigns/{id}/resume", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("7d0e69b5-4ad3-0c20-ae86-165db1c9149c"), "GET", 71, "/api/catalog/admin/coupon-campaigns/{id}/stats", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("9c6ef48a-989f-d487-f95a-670fdedd91ad"), "GET", 70, "/api/catalog/admin/coupon-campaigns/{id}", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("abcba821-b37c-bcbf-40ae-5d25cf1fd454"), "GET", 72, "/api/catalog/admin/coupon-campaigns/{id}/claims", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("b185e6fd-c44e-4e4d-ead3-277ddddcbeb8"), "POST", 75, "/api/catalog/admin/coupon-campaigns/{id}/publish", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("b56783a0-5bca-8909-7577-6d0a0f954d31"), "POST", 76, "/api/catalog/admin/coupon-campaigns/{id}/pause", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("c4441131-e603-d3af-859d-56013fb7f3e7"), "GET", 69, "/api/catalog/admin/coupon-campaigns", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("d442de31-0f6b-a355-6871-ce3af6acb6de"), "POST", 73, "/api/catalog/admin/coupon-campaigns", new Guid("88893bde-9223-0a9a-4835-b9d5a55b0bb5") },
                    { new Guid("f3e8ab37-273a-08c0-cdfb-73404094d8ea"), "POST", 79, "/api/catalog/admin/coupon-campaigns/{id}/quantity", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("f5a5c8b1-2f6d-d432-ac23-1cc8ff769cc2"), "PUT", 74, "/api/catalog/admin/coupon-campaigns/{id}", new Guid("83630e28-746d-3387-ea54-046054e22e88") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("0a6f9522-dc68-b314-9196-284de181f461"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("433a307b-bef0-83cc-c6a5-66ea1b3601c7"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("7d0e69b5-4ad3-0c20-ae86-165db1c9149c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("9c6ef48a-989f-d487-f95a-670fdedd91ad"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("abcba821-b37c-bcbf-40ae-5d25cf1fd454"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b185e6fd-c44e-4e4d-ead3-277ddddcbeb8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b56783a0-5bca-8909-7577-6d0a0f954d31"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c4441131-e603-d3af-859d-56013fb7f3e7"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("d442de31-0f6b-a355-6871-ce3af6acb6de"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f3e8ab37-273a-08c0-cdfb-73404094d8ea"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f5a5c8b1-2f6d-d432-ac23-1cc8ff769cc2"));
        }
    }
}
