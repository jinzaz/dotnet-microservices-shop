using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateManagementRoutes : Migration
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
                    { new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3"), "Create", "system.certificates.create", "证书管理-创建", "system.certificates" },
                    { new Guid("6fa551e3-61b1-22c0-e5b3-6a95a4d8fbc6"), "Delete", "system.certificate-keys.delete", "证书私钥-删除", "system.certificate-keys" },
                    { new Guid("7e3e8c45-1c13-4a95-ec96-83b4a6abdf37"), "Query", "system.certificate-keys.query", "证书私钥-查询", "system.certificate-keys" },
                    { new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9"), "Query", "system.certificates.query", "证书管理-查询", "system.certificates" },
                    { new Guid("bfdd5cbd-25b7-2e41-2832-1bbe095f3c80"), "Create", "system.certificate-keys.create", "证书私钥-创建", "system.certificate-keys" },
                    { new Guid("d35d9d27-d1f8-76d9-0e2c-6552c3bbbb36"), "Edit", "system.certificate-keys.edit", "证书私钥-编辑", "system.certificate-keys" },
                    { new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6"), "Delete", "system.certificates.delete", "证书管理-删除", "system.certificates" },
                    { new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8"), "Edit", "system.certificates.edit", "证书管理-编辑", "system.certificates" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("6fa551e3-61b1-22c0-e5b3-6a95a4d8fbc6"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("7e3e8c45-1c13-4a95-ec96-83b4a6abdf37"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("bfdd5cbd-25b7-2e41-2832-1bbe095f3c80"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("d35d9d27-d1f8-76d9-0e2c-6552c3bbbb36"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("00f3c905-6d21-4216-4046-22bf13a2f2ab"), "GET", 112, "/api/certificate/admin/certificates/{id}", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("07b4bec9-3040-0469-f7b5-4418c609db02"), "DELETE", 125, "/api/certificate/admin/acme/subscriptions/{id}", new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6") },
                    { new Guid("1bb94eeb-75b8-1cd6-94df-e6ec04b63c7b"), "POST", 124, "/api/certificate/admin/certificates/{id}/revoke", new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6") },
                    { new Guid("3efd1dc7-7931-9e90-7e35-4dc619da2a5e"), "GET", 110, "/api/certificate/admin/catalog", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("467fa577-543d-ed36-5e94-dae356c0253c"), "POST", 122, "/api/certificate/admin/acme/subscriptions/{id}/renew", new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8") },
                    { new Guid("6fbf2e3e-f67a-8b66-8137-9529191ba311"), "PUT", 121, "/api/certificate/admin/acme/subscriptions/{id}", new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8") },
                    { new Guid("715d89e4-4224-e687-0025-8c5139ee8b75"), "GET", 111, "/api/certificate/admin/certificates", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("7502414f-3a4f-7848-04a6-0400d66b1dc8"), "POST", 126, "/api/certificate/admin/certificates/{id}/export-private", new Guid("7e3e8c45-1c13-4a95-ec96-83b4a6abdf37") },
                    { new Guid("89af7dbd-0243-be46-e106-6f5f991bedf8"), "GET", 113, "/api/certificate/admin/certificates/{id}/export", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("9a714b5a-a5e8-a742-8a71-7b937b35475e"), "GET", 115, "/api/certificate/admin/acme/subscriptions", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("ab0f5101-5128-d21a-d3f3-ebcea47b4a74"), "POST", 123, "/api/certificate/admin/renewal/run", new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8") },
                    { new Guid("b89d9d6d-eb2f-e6b8-abbb-c39d35949bab"), "GET", 114, "/api/certificate/admin/audits", new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9") },
                    { new Guid("c9c9f1e1-bc17-481a-2492-cf2eab6fe8b1"), "POST", 117, "/api/certificate/admin/certificates/import", new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3") },
                    { new Guid("ccdbdd8d-fb9b-a9bf-5d74-21337b4d197d"), "PUT", 119, "/api/certificate/admin/certificates/{id}", new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8") },
                    { new Guid("ee577a27-9d2c-9a89-a06c-2745543ad94b"), "POST", 118, "/api/certificate/admin/acme/subscriptions", new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3") },
                    { new Guid("f989668d-4b89-8e87-fddc-c8032d35824c"), "POST", 120, "/api/certificate/admin/certificates/{id}/activate", new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8") },
                    { new Guid("fcb063a0-6249-3919-e25e-455d004a116c"), "POST", 116, "/api/certificate/admin/certificates/generate", new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("6fa551e3-61b1-22c0-e5b3-6a95a4d8fbc6"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("7e3e8c45-1c13-4a95-ec96-83b4a6abdf37"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("bfdd5cbd-25b7-2e41-2832-1bbe095f3c80"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("d35d9d27-d1f8-76d9-0e2c-6552c3bbbb36"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                keyColumns: new[] { "permission_id", "policy_id" },
                keyValues: new object[] { new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") });

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("00f3c905-6d21-4216-4046-22bf13a2f2ab"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("07b4bec9-3040-0469-f7b5-4418c609db02"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("1bb94eeb-75b8-1cd6-94df-e6ec04b63c7b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("3efd1dc7-7931-9e90-7e35-4dc619da2a5e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("467fa577-543d-ed36-5e94-dae356c0253c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("6fbf2e3e-f67a-8b66-8137-9529191ba311"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("715d89e4-4224-e687-0025-8c5139ee8b75"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("7502414f-3a4f-7848-04a6-0400d66b1dc8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("89af7dbd-0243-be46-e106-6f5f991bedf8"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("9a714b5a-a5e8-a742-8a71-7b937b35475e"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("ab0f5101-5128-d21a-d3f3-ebcea47b4a74"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("b89d9d6d-eb2f-e6b8-abbb-c39d35949bab"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("c9c9f1e1-bc17-481a-2492-cf2eab6fe8b1"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("ccdbdd8d-fb9b-a9bf-5d74-21337b4d197d"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("ee577a27-9d2c-9a89-a06c-2745543ad94b"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("f989668d-4b89-8e87-fddc-c8032d35824c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                keyColumn: "id",
                keyValue: new Guid("fcb063a0-6249-3919-e25e-455d004a116c"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("1a401cb2-b234-dcd4-8295-04e3c9029ec3"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("6fa551e3-61b1-22c0-e5b3-6a95a4d8fbc6"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("7e3e8c45-1c13-4a95-ec96-83b4a6abdf37"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("acd15c3d-e54a-cfe4-dd3b-7ba59375c9f9"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("bfdd5cbd-25b7-2e41-2832-1bbe095f3c80"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("d35d9d27-d1f8-76d9-0e2c-6552c3bbbb36"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("da5d96e2-6b0c-3df9-2658-2b0da2f17ce6"));

            migrationBuilder.DeleteData(
                schema: "bff_bm",
                table: "pbac_permissions",
                keyColumn: "id",
                keyValue: new Guid("f061c20a-fb57-53d7-4310-a938e1ab25e8"));
        }
    }
}
