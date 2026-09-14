using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddBmAdminApiRoutes : Migration
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
                    { new Guid("035399e6-be54-af03-9f19-3306477dd32f"), "POST", 55, "/api/notification/admin/messages/{notificationId}/retry", new Guid("73dee7ca-2b10-dc01-5232-055c12b27e65") },
                    { new Guid("05cfaf71-28e3-f243-cd77-bd88e18583ea"), "GET", 51, "/api/payment/admin/payments", new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8") },
                    { new Guid("18f7b0ed-6c95-98a1-1215-cd1eac4c9581"), "POST", 54, "/api/notification/admin/messages", new Guid("6e175bef-7a25-4750-aa3c-f094f1db3aaf") },
                    { new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"), "GET", 49, "/api/order/orders", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") },
                    { new Guid("28865ed9-b5cf-12b4-a566-b6cd5df345c5"), "GET", 47, "/api/inventory/admin/stock", new Guid("4f3d0ea0-bcb6-d85d-f161-9104884fffc6") },
                    { new Guid("2c9b5cf4-af69-2e41-6be0-6e6207cf9500"), "GET", 48, "/api/inventory/admin/flash-sales", new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743") },
                    { new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"), "GET", 50, "/api/order/orders/{id}", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") },
                    { new Guid("34df7ad7-b07f-1ee9-df20-c2d9a3253c9e"), "GET", 53, "/api/notification/admin/templates", new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382") },
                    { new Guid("8d6783f9-6e0e-d9b1-a1b5-295e121a3ce3"), "GET", 52, "/api/notification/admin/messages", new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382") },
                    { new Guid("9fcfd659-019a-86f5-8f8d-a6b8cb37e566"), "GET", 44, "/api/Auth/users/admin/roles", new Guid("b7379cac-a286-03dd-d7fc-3a2d65837018") },
                    { new Guid("b80dd25b-72f1-c10e-23ef-6f88e5743099"), "DELETE", 46, "/api/Auth/users/admin/{id}/roles/{role}", new Guid("2c382e8f-e49a-3a8e-94e9-20902758e580") },
                    { new Guid("e8cdbf1a-83f9-d631-1b07-e815953daafe"), "GET", 45, "/api/Auth/users/admin/{id}", new Guid("b7379cac-a286-03dd-d7fc-3a2d65837018") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("035399e6-be54-af03-9f19-3306477dd32f"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("05cfaf71-28e3-f243-cd77-bd88e18583ea"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("18f7b0ed-6c95-98a1-1215-cd1eac4c9581"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("28865ed9-b5cf-12b4-a566-b6cd5df345c5"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2c9b5cf4-af69-2e41-6be0-6e6207cf9500"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("34df7ad7-b07f-1ee9-df20-c2d9a3253c9e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8d6783f9-6e0e-d9b1-a1b5-295e121a3ce3"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("9fcfd659-019a-86f5-8f8d-a6b8cb37e566"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b80dd25b-72f1-c10e-23ef-6f88e5743099"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e8cdbf1a-83f9-d631-1b07-e815953daafe"));
        }
    }
}
