using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_permissions",
                columns: new[] { "id", "action", "code", "display_name", "module" },
                values: new object[,]
                {
                    { new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d"), "Query", "payment.balances.query", "账户余额-查询", "payment.balances" },
                    { new Guid("ae280351-374f-f757-b81d-eae84ad3eee3"), "Edit", "payment.balances.edit", "账户余额-编辑", "payment.balances" },
                    { new Guid("b8a51627-7bee-d583-3a27-b422aa02cd36"), "Create", "payment.balances.create", "账户余额-创建", "payment.balances" },
                    { new Guid("fcaa9cf7-526f-2a13-b85a-e4f09e54337c"), "Delete", "payment.balances.delete", "账户余额-删除", "payment.balances" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("ae280351-374f-f757-b81d-eae84ad3eee3"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("b8a51627-7bee-d583-3a27-b422aa02cd36"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("fcaa9cf7-526f-2a13-b85a-e4f09e54337c"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("43b5a85a-b6e1-f19b-faba-050da12697c5"), "GET", 152, "/api/payment/admin/balances/{userId}/entries", new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d") },
                    { new Guid("5b76abee-e1d2-c55a-d823-2b4ca8a7d042"), "POST", 153, "/api/payment/admin/balances/{userId}/adjustments", new Guid("ae280351-374f-f757-b81d-eae84ad3eee3") },
                    { new Guid("abb19e02-1641-fe47-7250-d650cfcca285"), "GET", 151, "/api/payment/admin/balances/{userId}", new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("ae280351-374f-f757-b81d-eae84ad3eee3"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("b8a51627-7bee-d583-3a27-b422aa02cd36"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("fcaa9cf7-526f-2a13-b85a-e4f09e54337c"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("43b5a85a-b6e1-f19b-faba-050da12697c5"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("5b76abee-e1d2-c55a-d823-2b4ca8a7d042"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("abb19e02-1641-fe47-7250-d650cfcca285"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("294185c7-3129-d2ab-bd9d-4493f5c8a57d"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("ae280351-374f-f757-b81d-eae84ad3eee3"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("b8a51627-7bee-d583-3a27-b422aa02cd36"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("fcaa9cf7-526f-2a13-b85a-e4f09e54337c"));
        }
    }
}
