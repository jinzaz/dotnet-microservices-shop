using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBmCommerceRoutePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2c9b5cf4-af69-2e41-6be0-6e6207cf9500"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("550562c4-5299-b8c8-5b1f-5046e97e0240"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8f4c071c-fa09-32f0-20e5-22a1469e3e18"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("de1325b4-e961-c46e-b2b3-db495b05e9f2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"));

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_permissions",
                columns: new[] { "id", "action", "code", "display_name", "module" },
                values: new object[,]
                {
                    { new Guid("18fdbe7c-a146-5417-5489-478efaaca309"), "Query", "inventory.allocations.query", "活动库存配额-查询", "inventory.allocations" },
                    { new Guid("2a230a72-9622-bcb2-91df-b9234ca76ca2"), "Edit", "inventory.allocations.edit", "活动库存配额-编辑", "inventory.allocations" },
                    { new Guid("af4a462b-7e9d-9c14-34d0-16fcfc33a8a2"), "Delete", "inventory.allocations.delete", "活动库存配额-删除", "inventory.allocations" },
                    { new Guid("ceb05d78-088f-6e8c-2085-c5fa15b15992"), "Create", "inventory.allocations.create", "活动库存配额-创建", "inventory.allocations" }
                });

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("035399e6-be54-af03-9f19-3306477dd32f"),
                column: "match_order",
                value: 54);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("05cfaf71-28e3-f243-cd77-bd88e18583ea"),
                column: "match_order",
                value: 50);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("18f7b0ed-6c95-98a1-1215-cd1eac4c9581"),
                column: "match_order",
                value: 53);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"),
                column: "match_order",
                value: 48);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"),
                column: "match_order",
                value: 49);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("34df7ad7-b07f-1ee9-df20-c2d9a3253c9e"),
                column: "match_order",
                value: 52);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8d6783f9-6e0e-d9b1-a1b5-295e121a3ce3"),
                column: "match_order",
                value: 51);

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("2d4a5075-0482-f160-eebb-8dbbbf6d516c"), "POST", 55, "/api/catalog/admin/products/{id}/variants", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("317f0cbe-9965-f999-1809-07d035c51078"), "DELETE", 57, "/api/catalog/admin/products/{id}/variants/{variantId}", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("49f44c42-f825-772b-a400-844430a30758"), "PUT", 59, "/api/catalog/admin/products/{id}/media/{mediaId}", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("94bf300a-e630-dc9d-d023-98d38c36fa89"), "POST", 58, "/api/catalog/admin/products/{id}/media", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("e387996e-c8f2-e7f5-fdee-b0ce4c73b6b6"), "PUT", 56, "/api/catalog/admin/products/{id}/variants/{variantId}", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("f05f53cf-8444-0f5b-e102-3b148fbbc2b4"), "DELETE", 60, "/api/catalog/admin/products/{id}/media/{mediaId}", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("18fdbe7c-a146-5417-5489-478efaaca309"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("2a230a72-9622-bcb2-91df-b9234ca76ca2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("af4a462b-7e9d-9c14-34d0-16fcfc33a8a2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("ceb05d78-088f-6e8c-2085-c5fa15b15992"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("00055747-1428-57ce-a0ea-0bc666bbb294"), "GET", 36, "/api/inventory/allocations/{allocationId}", new Guid("18fdbe7c-a146-5417-5489-478efaaca309") },
                    { new Guid("1aa696ea-d0a4-566d-aa75-96d6513736c6"), "POST", 37, "/api/inventory/admin/allocations", new Guid("ceb05d78-088f-6e8c-2085-c5fa15b15992") },
                    { new Guid("e1cdc7a7-5691-f690-07ac-b005ae5bb61b"), "GET", 61, "/api/inventory/admin/allocations", new Guid("18fdbe7c-a146-5417-5489-478efaaca309") },
                    { new Guid("e8152210-3224-2789-af5b-4da42db3135c"), "POST", 38, "/api/inventory/admin/allocations/{allocationId}/close", new Guid("2a230a72-9622-bcb2-91df-b9234ca76ca2") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("18fdbe7c-a146-5417-5489-478efaaca309"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("2a230a72-9622-bcb2-91df-b9234ca76ca2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("af4a462b-7e9d-9c14-34d0-16fcfc33a8a2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("ceb05d78-088f-6e8c-2085-c5fa15b15992"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("00055747-1428-57ce-a0ea-0bc666bbb294"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1aa696ea-d0a4-566d-aa75-96d6513736c6"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2d4a5075-0482-f160-eebb-8dbbbf6d516c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("317f0cbe-9965-f999-1809-07d035c51078"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("49f44c42-f825-772b-a400-844430a30758"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("94bf300a-e630-dc9d-d023-98d38c36fa89"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e1cdc7a7-5691-f690-07ac-b005ae5bb61b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e387996e-c8f2-e7f5-fdee-b0ce4c73b6b6"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e8152210-3224-2789-af5b-4da42db3135c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f05f53cf-8444-0f5b-e102-3b148fbbc2b4"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("18fdbe7c-a146-5417-5489-478efaaca309"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("2a230a72-9622-bcb2-91df-b9234ca76ca2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("af4a462b-7e9d-9c14-34d0-16fcfc33a8a2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("ceb05d78-088f-6e8c-2085-c5fa15b15992"));

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_permissions",
                columns: new[] { "id", "action", "code", "display_name", "module" },
                values: new object[,]
                {
                    { new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"), "Edit", "inventory.flash-sales.edit", "秒杀库存-编辑", "inventory.flash-sales" },
                    { new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"), "Query", "inventory.flash-sales.query", "秒杀库存-查询", "inventory.flash-sales" },
                    { new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"), "Delete", "inventory.flash-sales.delete", "秒杀库存-删除", "inventory.flash-sales" },
                    { new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"), "Create", "inventory.flash-sales.create", "秒杀库存-创建", "inventory.flash-sales" }
                });

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("035399e6-be54-af03-9f19-3306477dd32f"),
                column: "match_order",
                value: 55);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("05cfaf71-28e3-f243-cd77-bd88e18583ea"),
                column: "match_order",
                value: 51);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("18f7b0ed-6c95-98a1-1215-cd1eac4c9581"),
                column: "match_order",
                value: 54);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1a405d1c-ac38-1adc-e154-83468a0a3078"),
                column: "match_order",
                value: 49);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("30b4969a-842c-775c-d3ab-d933d020e2ca"),
                column: "match_order",
                value: 50);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("34df7ad7-b07f-1ee9-df20-c2d9a3253c9e"),
                column: "match_order",
                value: 53);

            migrationBuilder.UpdateData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("8d6783f9-6e0e-d9b1-a1b5-295e121a3ce3"),
                column: "match_order",
                value: 52);

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("2c9b5cf4-af69-2e41-6be0-6e6207cf9500"), "GET", 48, "/api/inventory/admin/flash-sales", new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743") },
                    { new Guid("550562c4-5299-b8c8-5b1f-5046e97e0240"), "GET", 36, "/api/inventory/flash-sales/{campaignId}", new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743") },
                    { new Guid("8f4c071c-fa09-32f0-20e5-22a1469e3e18"), "POST", 38, "/api/inventory/admin/flash-sales/{campaignId}/close", new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119") },
                    { new Guid("de1325b4-e961-c46e-b2b3-db495b05e9f2"), "POST", 37, "/api/inventory/admin/flash-sales", new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09") }
                });
        }
    }
}
