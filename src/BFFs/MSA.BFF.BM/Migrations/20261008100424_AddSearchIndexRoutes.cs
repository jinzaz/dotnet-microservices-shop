using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndexRoutes : Migration
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
                    { new Guid("5ad8288f-49b2-cf41-3749-8b3b1a9109aa"), "Edit", "system.search.edit", "搜索索引-编辑", "system.search" },
                    { new Guid("6ec21774-4480-0970-7d66-0ee7651064b4"), "Query", "system.search.query", "搜索索引-查询", "system.search" },
                    { new Guid("ea970552-8c1c-db55-fa4d-610b30f4b690"), "Delete", "system.search.delete", "搜索索引-删除", "system.search" },
                    { new Guid("ec3e1c25-2687-6178-1863-709e8eeadba2"), "Create", "system.search.create", "搜索索引-创建", "system.search" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("33fdfee6-44c4-1891-61eb-14bd3fc71a59"), "GET", 132, "/api/search/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("388dc0d5-0b10-f020-2764-bbc4463413f3"), "GET", 131, "/api/search/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("a5f1be16-526a-2b53-a3be-5d05d591a494"), "POST", 134, "/api/search/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("bb0b575b-48f3-883a-159b-ec7326d565c2"), "GET", 133, "/api/search/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("5ad8288f-49b2-cf41-3749-8b3b1a9109aa"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("6ec21774-4480-0970-7d66-0ee7651064b4"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("ea970552-8c1c-db55-fa4d-610b30f4b690"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("ec3e1c25-2687-6178-1863-709e8eeadba2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("035d5895-0032-853a-8cf1-567c31355533"), "POST", 130, "/api/search/admin/rebuild", new Guid("5ad8288f-49b2-cf41-3749-8b3b1a9109aa") },
                    { new Guid("0b83b6e8-4754-8a81-221e-8c3573eadb93"), "GET", 127, "/api/search/admin/status", new Guid("6ec21774-4480-0970-7d66-0ee7651064b4") },
                    { new Guid("237c4752-235a-f222-311b-c23afb7da5bd"), "GET", 129, "/api/search/suggestions", new Guid("6ec21774-4480-0970-7d66-0ee7651064b4") },
                    { new Guid("b334f238-fab9-5d17-e224-9507762220f2"), "GET", 128, "/api/search/products", new Guid("6ec21774-4480-0970-7d66-0ee7651064b4") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("5ad8288f-49b2-cf41-3749-8b3b1a9109aa"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("6ec21774-4480-0970-7d66-0ee7651064b4"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("ea970552-8c1c-db55-fa4d-610b30f4b690"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("ec3e1c25-2687-6178-1863-709e8eeadba2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("035d5895-0032-853a-8cf1-567c31355533"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("0b83b6e8-4754-8a81-221e-8c3573eadb93"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("237c4752-235a-f222-311b-c23afb7da5bd"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("33fdfee6-44c4-1891-61eb-14bd3fc71a59"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("388dc0d5-0b10-f020-2764-bbc4463413f3"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("a5f1be16-526a-2b53-a3be-5d05d591a494"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b334f238-fab9-5d17-e224-9507762220f2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("bb0b575b-48f3-883a-159b-ec7326d565c2"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ad8288f-49b2-cf41-3749-8b3b1a9109aa"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("6ec21774-4480-0970-7d66-0ee7651064b4"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("ea970552-8c1c-db55-fa4d-610b30f4b690"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("ec3e1c25-2687-6178-1863-709e8eeadba2"));
        }
    }
}
