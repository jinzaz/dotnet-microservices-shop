using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class SeparateStorefrontAndAdminReadRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("00055747-1428-57ce-a0ea-0bc666bbb294"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("02f0dca8-e7ea-1f5a-e324-f7a3b8a04e01"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2e55c024-0eef-9c59-ddb0-57751ab0f119"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"));

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("02b9daef-134e-2812-f5d0-c9fd9328ac92"), "GET", 36, "/api/inventory/admin/allocations/{allocationId}", new Guid("18fdbe7c-a146-5417-5489-478efaaca309") },
                    { new Guid("4439c5c5-fa6f-15e3-cd3e-21ea43571a01"), "GET", 43, "/api/notification/admin/users/{userId}", new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382") },
                    { new Guid("604d15d2-ee7c-6096-5293-9045adb9b4a7"), "GET", 42, "/api/payment/admin/orders/{orderId}", new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8") },
                    { new Guid("88cf0664-8a90-543f-76a7-358ebced433a"), "GET", 48, "/api/order/admin/orders", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") },
                    { new Guid("ee4d0d8e-9f27-c97d-8194-c71a20120595"), "GET", 49, "/api/order/admin/orders/{id}", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("02b9daef-134e-2812-f5d0-c9fd9328ac92"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("4439c5c5-fa6f-15e3-cd3e-21ea43571a01"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("604d15d2-ee7c-6096-5293-9045adb9b4a7"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("88cf0664-8a90-543f-76a7-358ebced433a"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("ee4d0d8e-9f27-c97d-8194-c71a20120595"));

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("00055747-1428-57ce-a0ea-0bc666bbb294"), "GET", 36, "/api/inventory/allocations/{allocationId}", new Guid("18fdbe7c-a146-5417-5489-478efaaca309") },
                    { new Guid("02f0dca8-e7ea-1f5a-e324-f7a3b8a04e01"), "GET", 43, "/api/notification/users/{userId}", new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382") },
                    { new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"), "GET", 48, "/api/order/orders", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") },
                    { new Guid("2e55c024-0eef-9c59-ddb0-57751ab0f119"), "GET", 42, "/api/payment/orders/{orderId}", new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8") },
                    { new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"), "GET", 49, "/api/order/orders/{id}", new Guid("30fea92a-e975-7a82-3922-6a4f99624868") }
                });
        }
    }
}
