using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandRoutes : Migration
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
                    { new Guid("4caca00d-119b-b8fe-483d-1f1a846bb88b"), "Delete", "catalog.brands.delete", "商品品牌-删除", "catalog.brands" },
                    { new Guid("5b35ee72-8124-c431-9de2-921170864a35"), "Create", "catalog.brands.create", "商品品牌-创建", "catalog.brands" },
                    { new Guid("c1f32955-537d-3803-840a-4d40aabb38b1"), "Query", "catalog.brands.query", "商品品牌-查询", "catalog.brands" },
                    { new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76"), "Edit", "catalog.brands.edit", "商品品牌-编辑", "catalog.brands" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[] { new Guid("5f7889f5-0e42-6e28-c4fe-61963f58e33b"), "PUT", 140, "/api/catalog/admin/products/{id}/brand", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("4caca00d-119b-b8fe-483d-1f1a846bb88b"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("5b35ee72-8124-c431-9de2-921170864a35"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("c1f32955-537d-3803-840a-4d40aabb38b1"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("0fee5710-301a-a33a-607c-272bcde08f73"), "GET", 135, "/api/catalog/admin/brands", new Guid("c1f32955-537d-3803-840a-4d40aabb38b1") },
                    { new Guid("1afd5c92-90ec-d881-c2ab-b33edd541646"), "POST", 138, "/api/catalog/admin/brands/{id}/activate", new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76") },
                    { new Guid("20726a04-bd2b-e459-97be-3ea38b10e0c7"), "POST", 139, "/api/catalog/admin/brands/{id}/archive", new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76") },
                    { new Guid("67d9c972-be81-7021-1f3a-8aae9c488cff"), "PUT", 137, "/api/catalog/admin/brands/{id}/name", new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76") },
                    { new Guid("e039f367-7b4b-b1ca-9062-fa00d0e36c89"), "POST", 136, "/api/catalog/admin/brands", new Guid("5b35ee72-8124-c431-9de2-921170864a35") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("4caca00d-119b-b8fe-483d-1f1a846bb88b"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("5b35ee72-8124-c431-9de2-921170864a35"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("c1f32955-537d-3803-840a-4d40aabb38b1"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("0fee5710-301a-a33a-607c-272bcde08f73"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1afd5c92-90ec-d881-c2ab-b33edd541646"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("20726a04-bd2b-e459-97be-3ea38b10e0c7"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("5f7889f5-0e42-6e28-c4fe-61963f58e33b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("67d9c972-be81-7021-1f3a-8aae9c488cff"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e039f367-7b4b-b1ca-9062-fa00d0e36c89"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("4caca00d-119b-b8fe-483d-1f1a846bb88b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("5b35ee72-8124-c431-9de2-921170864a35"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("c1f32955-537d-3803-840a-4d40aabb38b1"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbfa4c31-e094-081f-e9ca-fd7968004a76"));
        }
    }
}
