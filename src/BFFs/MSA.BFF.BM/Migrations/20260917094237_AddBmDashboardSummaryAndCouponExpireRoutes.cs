using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddBmDashboardSummaryAndCouponExpireRoutes : Migration
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
                    { new Guid("413bfea8-d819-cd40-6a76-69c87b833b03"), "GET", 62, "/api/order/admin/orders/summary", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") },
                    { new Guid("c8693ac5-dcb1-41fa-5ea8-5714b10c2fb7"), "GET", 63, "/api/payment/admin/payments/summary", new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8") },
                    { new Guid("d8797562-44d6-1d7d-ff44-b300d086dc8f"), "POST", 64, "/api/catalog/admin/coupons/{id}/expire", new Guid("83630e28-746d-3387-ea54-046054e22e88") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("413bfea8-d819-cd40-6a76-69c87b833b03"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c8693ac5-dcb1-41fa-5ea8-5714b10c2fb7"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("d8797562-44d6-1d7d-ff44-b300d086dc8f"));
        }
    }
}
