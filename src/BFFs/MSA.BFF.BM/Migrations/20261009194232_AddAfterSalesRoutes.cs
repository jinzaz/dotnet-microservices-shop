using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddAfterSalesRoutes : Migration
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
                    { new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e"), "Edit", "order.after-sales.edit", "售后-编辑", "order.after-sales" },
                    { new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33"), "Query", "order.after-sales.query", "售后-查询", "order.after-sales" },
                    { new Guid("af7fdc4c-c21c-785e-2b0a-b15828e38a42"), "Delete", "order.after-sales.delete", "售后-删除", "order.after-sales" },
                    { new Guid("d5bb516f-24a4-4403-cecf-6fab3e8ab6a9"), "Create", "order.after-sales.create", "售后-创建", "order.after-sales" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("af7fdc4c-c21c-785e-2b0a-b15828e38a42"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("d5bb516f-24a4-4403-cecf-6fab3e8ab6a9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("07c6a76b-9969-95da-aa42-ea74b6db08ee"), "POST", 150, "/api/order/admin/after-sales/{id}/reserve-stock", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("099f3f5a-db1d-0ba0-4173-aa29fbd84c58"), "POST", 146, "/api/order/admin/after-sales/{id}/refund", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("26a0585e-e098-8d8a-7a95-342155986ce8"), "GET", 142, "/api/order/admin/after-sales/{id}", new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33") },
                    { new Guid("2b46800f-0e76-d10d-bd81-4905aeba0f92"), "POST", 144, "/api/order/admin/after-sales/{id}/reject", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("2e430990-ca91-f096-23a8-0fc5eb6206fb"), "POST", 148, "/api/order/admin/after-sales/{id}/complete", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("567bf04f-912f-2b63-4032-035b2dc59593"), "GET", 141, "/api/order/admin/after-sales", new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33") },
                    { new Guid("8d7d01b2-a268-25e6-306d-aa6dfe917e03"), "POST", 143, "/api/order/admin/after-sales/{id}/approve", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("8f3ace8f-14e6-58c4-8866-4ab39a90a312"), "POST", 149, "/api/order/admin/after-sales/{id}/dispose-return", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("fc2a64ee-6233-2809-fafc-8eef14c9f12e"), "POST", 147, "/api/order/admin/after-sales/{id}/ship-replacement", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") },
                    { new Guid("fd2bb64b-2856-e43d-b42d-d2c477b99962"), "POST", 145, "/api/order/admin/after-sales/{id}/receive-return", new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("af7fdc4c-c21c-785e-2b0a-b15828e38a42"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("d5bb516f-24a4-4403-cecf-6fab3e8ab6a9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("07c6a76b-9969-95da-aa42-ea74b6db08ee"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("099f3f5a-db1d-0ba0-4173-aa29fbd84c58"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("26a0585e-e098-8d8a-7a95-342155986ce8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2b46800f-0e76-d10d-bd81-4905aeba0f92"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2e430990-ca91-f096-23a8-0fc5eb6206fb"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("567bf04f-912f-2b63-4032-035b2dc59593"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8d7d01b2-a268-25e6-306d-aa6dfe917e03"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8f3ace8f-14e6-58c4-8866-4ab39a90a312"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("fc2a64ee-6233-2809-fafc-8eef14c9f12e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("fd2bb64b-2856-e43d-b42d-d2c477b99962"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("1ee4b4d4-64ec-9890-bf41-cf9661b77a0e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("85ceef9f-1e2f-4970-3f90-586a2f90cf33"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("af7fdc4c-c21c-785e-2b0a-b15828e38a42"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("d5bb516f-24a4-4403-cecf-6fab3e8ab6a9"));
        }
    }
}
