using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingOperationsRoutes : Migration
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
                    { new Guid("4da30147-3c51-9f58-50d6-98e543a2258b"), "Delete", "system.messaging.delete", "消息运维-删除", "system.messaging" },
                    { new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed"), "Edit", "system.messaging.edit", "消息运维-编辑", "system.messaging" },
                    { new Guid("9dc32fb7-ff30-7325-0df2-b0ad4898e77e"), "Create", "system.messaging.create", "消息运维-创建", "system.messaging" },
                    { new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a"), "Query", "system.messaging.query", "消息运维-查询", "system.messaging" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("4da30147-3c51-9f58-50d6-98e543a2258b"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("9dc32fb7-ff30-7325-0df2-b0ad4898e77e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("03513f53-1f4b-20d7-06aa-0ade6f7d8f63"), "POST", 109, "/api/notification/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("22c0fab6-0558-38a1-dad4-f4cdb92a0fed"), "POST", 88, "/api/order/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("2dca26a4-1f71-7c20-fd75-7bbdca339481"), "GET", 86, "/api/order/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("3aad32c3-c3ff-dad5-80b3-8ec93bcd0004"), "POST", 94, "/api/inventory/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("3ad09d38-08b4-2172-7f10-b784880073b9"), "POST", 84, "/api/catalog/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("3e4b7a68-120f-bd81-37b7-38b31a45ee01"), "GET", 105, "/api/notification/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("4b9be82b-94a9-bbe6-6ee3-a370f8af2bb6"), "GET", 95, "/api/payment/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("51447e40-e0f8-ecb0-7871-77d613c3e40d"), "GET", 92, "/api/inventory/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("51e96dcf-881a-b217-168f-2efac5577216"), "GET", 82, "/api/catalog/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("55b44ccb-e4d8-768f-489c-7f2b957926cf"), "GET", 90, "/api/inventory/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("5d3a99e3-d3ff-2bb2-5a2e-abbdf881f8b4"), "POST", 108, "/api/notification/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("5ef5964d-578a-eee2-77f5-0aa7ef43d81b"), "GET", 100, "/api/basket/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("657ba568-83a6-54a3-2e7b-81641470a834"), "POST", 104, "/api/basket/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("677f84a5-e6d5-8f83-ae14-0f8ef0e7d15f"), "GET", 85, "/api/order/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("6a0ca556-fa9b-8793-6ae5-ee3146e06dd3"), "GET", 91, "/api/inventory/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("6e0331b0-d29b-0131-8b72-5688904010a8"), "GET", 107, "/api/notification/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("72e0ac3f-9abc-6d96-baea-88ec3345501d"), "POST", 99, "/api/payment/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("803b24b0-cad5-a099-6243-d8a20a0174ba"), "POST", 83, "/api/catalog/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("945a5799-e378-572c-0480-cf03c32c0ce0"), "GET", 80, "/api/catalog/admin/messaging/outbox/failed", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("afa43aa8-c223-dd3f-d3c5-628b8b55c829"), "POST", 93, "/api/inventory/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("b9bed228-5355-a6d7-dbaf-111304afa010"), "GET", 102, "/api/basket/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("c41185d9-1abf-496f-27a0-1d50557e00be"), "POST", 98, "/api/payment/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("c93eba14-0bd8-90e3-5e74-7cc74cda5dca"), "GET", 81, "/api/catalog/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("c94b3f08-4fd4-db99-d97d-3ef8d1762be8"), "POST", 89, "/api/order/admin/messaging/dead-letters/replay", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("e779a5ed-98a0-e93f-591b-12c896f99925"), "GET", 106, "/api/notification/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("f12847e8-672e-9be3-5c75-7017daabe662"), "GET", 97, "/api/payment/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("f269f2f3-c749-c1fe-d7c5-2a38d8ff0b3b"), "GET", 101, "/api/basket/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("f4353c61-a8cf-d035-7e64-7df0aba91896"), "GET", 96, "/api/payment/admin/messaging/sagas/stalled", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") },
                    { new Guid("fbf7ab33-470a-3056-1a8a-9762ab0c5c71"), "POST", 103, "/api/basket/admin/messaging/outbox/failed/requeue", new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed") },
                    { new Guid("fefbeb23-89a9-fdd0-1d98-bc2f8b137b89"), "GET", 87, "/api/order/admin/messaging/dead-letters", new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("4da30147-3c51-9f58-50d6-98e543a2258b"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("9dc32fb7-ff30-7325-0df2-b0ad4898e77e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("03513f53-1f4b-20d7-06aa-0ade6f7d8f63"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("22c0fab6-0558-38a1-dad4-f4cdb92a0fed"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("2dca26a4-1f71-7c20-fd75-7bbdca339481"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("3aad32c3-c3ff-dad5-80b3-8ec93bcd0004"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("3ad09d38-08b4-2172-7f10-b784880073b9"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("3e4b7a68-120f-bd81-37b7-38b31a45ee01"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("4b9be82b-94a9-bbe6-6ee3-a370f8af2bb6"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("51447e40-e0f8-ecb0-7871-77d613c3e40d"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("51e96dcf-881a-b217-168f-2efac5577216"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("55b44ccb-e4d8-768f-489c-7f2b957926cf"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("5d3a99e3-d3ff-2bb2-5a2e-abbdf881f8b4"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("5ef5964d-578a-eee2-77f5-0aa7ef43d81b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("657ba568-83a6-54a3-2e7b-81641470a834"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("677f84a5-e6d5-8f83-ae14-0f8ef0e7d15f"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("6a0ca556-fa9b-8793-6ae5-ee3146e06dd3"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("6e0331b0-d29b-0131-8b72-5688904010a8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("72e0ac3f-9abc-6d96-baea-88ec3345501d"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("803b24b0-cad5-a099-6243-d8a20a0174ba"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("945a5799-e378-572c-0480-cf03c32c0ce0"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("afa43aa8-c223-dd3f-d3c5-628b8b55c829"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b9bed228-5355-a6d7-dbaf-111304afa010"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c41185d9-1abf-496f-27a0-1d50557e00be"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c93eba14-0bd8-90e3-5e74-7cc74cda5dca"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c94b3f08-4fd4-db99-d97d-3ef8d1762be8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("e779a5ed-98a0-e93f-591b-12c896f99925"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f12847e8-672e-9be3-5c75-7017daabe662"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f269f2f3-c749-c1fe-d7c5-2a38d8ff0b3b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f4353c61-a8cf-d035-7e64-7df0aba91896"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("fbf7ab33-470a-3056-1a8a-9762ab0c5c71"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("fefbeb23-89a9-fdd0-1d98-bc2f8b137b89"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("4da30147-3c51-9f58-50d6-98e543a2258b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("95e94744-d5eb-357a-3b7c-2609e0dfe3ed"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("9dc32fb7-ff30-7325-0df2-b0ad4898e77e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("a6d5add6-b76b-6ed2-8940-daab08f0c27a"));
        }
    }
}
