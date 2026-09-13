using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class InitialBffBm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bff_bm");

            migrationBuilder.CreateTable(
                name: "bm_roles",
                schema: "bff_bm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "停用角色不会参与授权"),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bm_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bm_users",
                schema: "bff_bm",
                columns: table => new
                {
                    identity_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Identity 用户 sub；仅逻辑关联，不跨库建外键"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "后台账号是否允许访问"),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bm_users", x => x.identity_user_id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictApplications",
                schema: "bff_bm",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClientSecret = table.Column<string>(type: "text", nullable: true),
                    ClientType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ConsentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedirectUris = table.Column<string>(type: "text", nullable: true),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictScopes",
                schema: "bff_bm",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Descriptions = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    DisplayNames = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictScopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pbac_permissions",
                schema: "bff_bm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "模块与操作组成的稳定权限编码"),
                    module = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "后台业务模块编码"),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "查询、创建、编辑或删除操作"),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_permissions", x => x.id);
                    table.CheckConstraint("ck_pbac_permissions_action", "action IN ('Query', 'Create', 'Edit', 'Delete')");
                });

            migrationBuilder.CreateTable(
                name: "pbac_policies",
                schema: "bff_bm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    effect = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "允许或拒绝，拒绝优先"),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_policies", x => x.id);
                    table.CheckConstraint("ck_pbac_policies_effect", "effect IN ('Allow', 'Deny')");
                });

            migrationBuilder.CreateTable(
                name: "bm_user_roles",
                schema: "bff_bm",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bm_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_bm_user_roles_bm_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "bff_bm",
                        principalTable: "bm_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bm_user_roles_bm_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "bff_bm",
                        principalTable: "bm_users",
                        principalColumn: "identity_user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictAuthorizations",
                schema: "bff_bm",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    Scopes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                        column: x => x.ApplicationId,
                        principalSchema: "bff_bm",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "pbac_route_permissions",
                schema: "bff_bm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "该代理路由要求的权限"),
                    http_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    path_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "以 /api 开头的 BFF 代理路由模板"),
                    match_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_route_permissions", x => x.id);
                    table.ForeignKey(
                        name: "FK_pbac_route_permissions_pbac_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "bff_bm",
                        principalTable: "pbac_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pbac_policy_permissions",
                schema: "bff_bm",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_policy_permissions", x => new { x.policy_id, x.permission_id });
                    table.ForeignKey(
                        name: "FK_pbac_policy_permissions_pbac_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "bff_bm",
                        principalTable: "pbac_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pbac_policy_permissions_pbac_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "bff_bm",
                        principalTable: "pbac_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pbac_role_policies",
                schema: "bff_bm",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_role_policies", x => new { x.policy_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_pbac_role_policies_bm_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "bff_bm",
                        principalTable: "bm_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pbac_role_policies_pbac_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "bff_bm",
                        principalTable: "pbac_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pbac_user_policies",
                schema: "bff_bm",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pbac_user_policies", x => new { x.policy_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_pbac_user_policies_bm_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "bff_bm",
                        principalTable: "bm_users",
                        principalColumn: "identity_user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pbac_user_policies_pbac_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "bff_bm",
                        principalTable: "pbac_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpenIddictTokens",
                schema: "bff_bm",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApplicationId = table.Column<string>(type: "text", nullable: true),
                    AuthorizationId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "bff_bm",
                        principalTable: "OpenIddictApplications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "bff_bm",
                        principalTable: "OpenIddictAuthorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "bm_roles",
                columns: new[] { "id", "code", "created_on_utc", "display_name", "is_active", "normalized_code" },
                values: new object[] { new Guid("88267ea7-c32e-17d4-d57b-dd41e69d7278"), "system-administrator", new DateTimeOffset(new DateTime(2026, 9, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "系统管理员", true, "SYSTEM-ADMINISTRATOR" });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_permissions",
                columns: new[] { "id", "action", "code", "display_name", "module" },
                values: new object[,]
                {
                    { new Guid("041b3100-bb75-6acf-31b9-b8a4a5119f67"), "Edit", "inventory.stock.edit", "库存-编辑", "inventory.stock" },
                    { new Guid("0b242333-b878-086a-a324-e6508c4247b9"), "Query", "catalog.categories.query", "商品分类-查询", "catalog.categories" },
                    { new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"), "Edit", "inventory.flash-sales.edit", "秒杀库存-编辑", "inventory.flash-sales" },
                    { new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad"), "Edit", "catalog.products.edit", "商品-编辑", "catalog.products" },
                    { new Guid("2c382e8f-e49a-3a8e-94e9-20902758e580"), "Edit", "identity.users.edit", "用户账号-编辑", "identity.users" },
                    { new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447"), "Query", "catalog.coupons.query", "优惠券-查询", "catalog.coupons" },
                    { new Guid("30fea92a-e975-7a82-3922-6a4f99624868"), "Query", "order.fulfillment.query", "订单履约-查询", "order.fulfillment" },
                    { new Guid("3732e1b5-8c48-fda6-2949-5423b9eee09a"), "Create", "user.loyalty-points.create", "会员积分-创建", "user.loyalty-points" },
                    { new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265"), "Edit", "catalog.promotions.edit", "促销活动-编辑", "catalog.promotions" },
                    { new Guid("3b4906c9-b9af-844c-61d4-da543ef8fe3a"), "Delete", "catalog.categories.delete", "商品分类-删除", "catalog.categories" },
                    { new Guid("44842b17-01d5-3dae-85a5-72f3a98d77c2"), "Query", "catalog.products.query", "商品-查询", "catalog.products" },
                    { new Guid("456b78e5-a319-012a-a09e-7258d482918a"), "Delete", "inventory.stock.delete", "库存-删除", "inventory.stock" },
                    { new Guid("4f3d0ea0-bcb6-d85d-f161-9104884fffc6"), "Query", "inventory.stock.query", "库存-查询", "inventory.stock" },
                    { new Guid("57489e0e-8601-4ae8-5f16-013430a352f3"), "Create", "order.fulfillment.create", "订单履约-创建", "order.fulfillment" },
                    { new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"), "Query", "inventory.flash-sales.query", "秒杀库存-查询", "inventory.flash-sales" },
                    { new Guid("5cc74225-f7e3-0dae-012f-6ba44784b46e"), "Delete", "identity.users.delete", "用户账号-删除", "identity.users" },
                    { new Guid("5f0db1cc-9fc4-bb4d-8bbd-e20bb40e7dd2"), "Create", "inventory.stock.create", "库存-创建", "inventory.stock" },
                    { new Guid("60d2c01e-07f1-b339-645a-39bb3abe86b7"), "Edit", "user.loyalty-points.edit", "会员积分-编辑", "user.loyalty-points" },
                    { new Guid("680fe2a6-9eea-e44b-e2d1-6348251f15b5"), "Create", "catalog.categories.create", "商品分类-创建", "catalog.categories" },
                    { new Guid("6d329f7b-2a2f-268f-63f5-9becc0963660"), "Delete", "catalog.coupons.delete", "优惠券-删除", "catalog.coupons" },
                    { new Guid("6e175bef-7a25-4750-aa3c-f094f1db3aaf"), "Create", "notification.messages.create", "用户通知-创建", "notification.messages" },
                    { new Guid("73dee7ca-2b10-dc01-5232-055c12b27e65"), "Edit", "notification.messages.edit", "用户通知-编辑", "notification.messages" },
                    { new Guid("81988e0a-eb37-4759-40f6-7464e99b7654"), "Create", "payment.transactions.create", "支付交易-创建", "payment.transactions" },
                    { new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8"), "Query", "payment.transactions.query", "支付交易-查询", "payment.transactions" },
                    { new Guid("83630e28-746d-3387-ea54-046054e22e88"), "Edit", "catalog.coupons.edit", "优惠券-编辑", "catalog.coupons" },
                    { new Guid("88893bde-9223-0a9a-4835-b9d5a55b0bb5"), "Create", "catalog.coupons.create", "优惠券-创建", "catalog.coupons" },
                    { new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"), "Delete", "inventory.flash-sales.delete", "秒杀库存-删除", "inventory.flash-sales" },
                    { new Guid("97374314-60e1-2769-dc77-5993a095e8fa"), "Delete", "order.fulfillment.delete", "订单履约-删除", "order.fulfillment" },
                    { new Guid("99169841-c040-8afc-7b45-d55a6f2df51a"), "Delete", "user.loyalty-points.delete", "会员积分-删除", "user.loyalty-points" },
                    { new Guid("9c1ac586-5e9e-b905-84be-ae8e4d2fcdb5"), "Create", "identity.users.create", "用户账号-创建", "identity.users" },
                    { new Guid("a5698022-263e-cb9b-1a4f-f69472759566"), "Create", "catalog.products.create", "商品-创建", "catalog.products" },
                    { new Guid("a660122d-2862-8bc9-8eaf-dd027e2c8031"), "Create", "system.authorization.create", "权限管理-创建", "system.authorization" },
                    { new Guid("b59cd865-be88-5a3e-203d-c9b91cb9c786"), "Delete", "catalog.products.delete", "商品-删除", "catalog.products" },
                    { new Guid("b7379cac-a286-03dd-d7fc-3a2d65837018"), "Query", "identity.users.query", "用户账号-查询", "identity.users" },
                    { new Guid("b942de90-5bf1-a228-4502-0160376e2f49"), "Edit", "system.authorization.edit", "权限管理-编辑", "system.authorization" },
                    { new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"), "Create", "inventory.flash-sales.create", "秒杀库存-创建", "inventory.flash-sales" },
                    { new Guid("c4739493-2a81-8ce9-e22e-2473ddbcbdf9"), "Query", "system.authorization.query", "权限管理-查询", "system.authorization" },
                    { new Guid("c76622ed-a5f0-a5bb-c456-e1997d50134a"), "Create", "catalog.promotions.create", "促销活动-创建", "catalog.promotions" },
                    { new Guid("d07ccba0-82ba-02d7-c7ff-e4c09d6c07ca"), "Delete", "payment.transactions.delete", "支付交易-删除", "payment.transactions" },
                    { new Guid("d5439cba-7a5f-3842-8c57-7cbadd0c6d17"), "Edit", "catalog.categories.edit", "商品分类-编辑", "catalog.categories" },
                    { new Guid("dc8e3eb9-a18f-cac8-82b2-669858c237bd"), "Delete", "notification.messages.delete", "用户通知-删除", "notification.messages" },
                    { new Guid("e8b2b7b5-6483-fbad-e5fd-cf0588f6c50e"), "Edit", "order.fulfillment.edit", "订单履约-编辑", "order.fulfillment" },
                    { new Guid("e8e2e5f7-2958-7d00-e191-33d57f85ec18"), "Delete", "system.authorization.delete", "权限管理-删除", "system.authorization" },
                    { new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382"), "Query", "notification.messages.query", "用户通知-查询", "notification.messages" },
                    { new Guid("f27e0c22-bcec-7fe0-5b00-3609d5f0b5e9"), "Query", "user.loyalty-points.query", "会员积分-查询", "user.loyalty-points" },
                    { new Guid("f2bb4ff7-16b2-5b84-d7fb-3f8d795ae085"), "Edit", "payment.transactions.edit", "支付交易-编辑", "payment.transactions" },
                    { new Guid("f7036a0a-4159-432b-61da-e741cf4eec8c"), "Query", "catalog.promotions.query", "促销活动-查询", "catalog.promotions" },
                    { new Guid("fee8a4a3-59d6-eb1b-17ac-c6480c2188af"), "Delete", "catalog.promotions.delete", "促销活动-删除", "catalog.promotions" }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policies",
                columns: new[] { "id", "created_on_utc", "effect", "name", "normalized_name" },
                values: new object[] { new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654"), new DateTimeOffset(new DateTime(2026, 9, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Allow", "System administrators", "SYSTEM ADMINISTRATORS" });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                columns: new[] { "permission_id", "policy_id" },
                values: new object[,]
                {
                    { new Guid("041b3100-bb75-6acf-31b9-b8a4a5119f67"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("0b242333-b878-086a-a324-e6508c4247b9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("2c382e8f-e49a-3a8e-94e9-20902758e580"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("30fea92a-e975-7a82-3922-6a4f99624868"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("3732e1b5-8c48-fda6-2949-5423b9eee09a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("3b4906c9-b9af-844c-61d4-da543ef8fe3a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("44842b17-01d5-3dae-85a5-72f3a98d77c2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("456b78e5-a319-012a-a09e-7258d482918a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("4f3d0ea0-bcb6-d85d-f161-9104884fffc6"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("57489e0e-8601-4ae8-5f16-013430a352f3"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("5cc74225-f7e3-0dae-012f-6ba44784b46e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("5f0db1cc-9fc4-bb4d-8bbd-e20bb40e7dd2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("60d2c01e-07f1-b339-645a-39bb3abe86b7"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("680fe2a6-9eea-e44b-e2d1-6348251f15b5"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("6d329f7b-2a2f-268f-63f5-9becc0963660"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("6e175bef-7a25-4750-aa3c-f094f1db3aaf"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("73dee7ca-2b10-dc01-5232-055c12b27e65"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("81988e0a-eb37-4759-40f6-7464e99b7654"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("83630e28-746d-3387-ea54-046054e22e88"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("88893bde-9223-0a9a-4835-b9d5a55b0bb5"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("88cc40a2-ad84-5763-0e63-4fd6703f72d2"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("97374314-60e1-2769-dc77-5993a095e8fa"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("99169841-c040-8afc-7b45-d55a6f2df51a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("9c1ac586-5e9e-b905-84be-ae8e4d2fcdb5"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("a5698022-263e-cb9b-1a4f-f69472759566"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("a660122d-2862-8bc9-8eaf-dd027e2c8031"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("b59cd865-be88-5a3e-203d-c9b91cb9c786"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("b7379cac-a286-03dd-d7fc-3a2d65837018"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("b942de90-5bf1-a228-4502-0160376e2f49"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("c4739493-2a81-8ce9-e22e-2473ddbcbdf9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("c76622ed-a5f0-a5bb-c456-e1997d50134a"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("d07ccba0-82ba-02d7-c7ff-e4c09d6c07ca"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("d5439cba-7a5f-3842-8c57-7cbadd0c6d17"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("dc8e3eb9-a18f-cac8-82b2-669858c237bd"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("e8b2b7b5-6483-fbad-e5fd-cf0588f6c50e"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("e8e2e5f7-2958-7d00-e191-33d57f85ec18"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("f27e0c22-bcec-7fe0-5b00-3609d5f0b5e9"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("f2bb4ff7-16b2-5b84-d7fb-3f8d795ae085"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("f7036a0a-4159-432b-61da-e741cf4eec8c"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") },
                    { new Guid("fee8a4a3-59d6-eb1b-17ac-c6480c2188af"), new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654") }
                });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_role_policies",
                columns: new[] { "policy_id", "role_id" },
                values: new object[] { new Guid("47e4a07f-dfc9-ca60-fcdc-88d3e3beb654"), new Guid("88267ea7-c32e-17d4-d57b-dd41e69d7278") });

            migrationBuilder.InsertData(
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "id", "http_method", "match_order", "path_template", "permission_id" },
                values: new object[,]
                {
                    { new Guid("02f0dca8-e7ea-1f5a-e324-f7a3b8a04e01"), "GET", 43, "/api/notification/users/{userId}", new Guid("ededa9aa-140d-3b5b-91b8-87828e6dd382") },
                    { new Guid("0bb9e919-802f-0fd5-cce8-1df6fa528158"), "GET", 21, "/api/catalog/admin/promotions/{id}", new Guid("f7036a0a-4159-432b-61da-e741cf4eec8c") },
                    { new Guid("1921dd56-9f0a-cd64-95bb-810ee7cbd7d3"), "GET", 29, "/api/catalog/admin/coupons/{id}", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("1b94c316-3821-ba41-84b7-85f81fc5ab91"), "POST", 39, "/api/order/admin/orders/{orderId}/fulfillment/ship", new Guid("e8b2b7b5-6483-fbad-e5fd-cf0588f6c50e") },
                    { new Guid("23d023d5-8a35-2641-6ff3-2e80eebde454"), "GET", 5, "/api/user/admin/points/{userId}/entries/{entryId}", new Guid("f27e0c22-bcec-7fe0-5b00-3609d5f0b5e9") },
                    { new Guid("23e0beac-0a35-c9ff-48e3-4a45112d20bc"), "POST", 40, "/api/order/admin/orders/{orderId}/fulfillment/in-transit", new Guid("e8b2b7b5-6483-fbad-e5fd-cf0588f6c50e") },
                    { new Guid("277d4de3-0a1c-c6ec-8c91-abac9484444b"), "POST", 35, "/api/inventory/admin/stock/adjustments", new Guid("041b3100-bb75-6acf-31b9-b8a4a5119f67") },
                    { new Guid("2e55c024-0eef-9c59-ddb0-57751ab0f119"), "GET", 42, "/api/payment/orders/{orderId}", new Guid("82ce7c5b-8dcf-6d26-baee-ddf208b15cc8") },
                    { new Guid("311d6c42-ccd9-166b-18a3-cd77086a3dab"), "POST", 8, "/api/catalog/admin/categories", new Guid("680fe2a6-9eea-e44b-e2d1-6348251f15b5") },
                    { new Guid("3b0162b1-6286-3753-882f-f150f07ce4df"), "POST", 22, "/api/catalog/admin/promotions", new Guid("c76622ed-a5f0-a5bb-c456-e1997d50134a") },
                    { new Guid("49ebd5fe-fb7a-528e-a5a3-c3d8f08d1b59"), "POST", 30, "/api/catalog/admin/coupons", new Guid("88893bde-9223-0a9a-4835-b9d5a55b0bb5") },
                    { new Guid("53bc22b9-3651-619a-5530-b4a18fc0f920"), "POST", 31, "/api/catalog/admin/coupons/{id}/activate", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("550562c4-5299-b8c8-5b1f-5046e97e0240"), "GET", 36, "/api/inventory/flash-sales/{campaignId}", new Guid("5953bfa5-8396-c2c9-a6dd-cd7b63258743") },
                    { new Guid("5937ce98-7661-5f0c-4e4b-cb14d27d9af6"), "GET", 33, "/api/inventory/admin/stock/{sku}/movements", new Guid("4f3d0ea0-bcb6-d85d-f161-9104884fffc6") },
                    { new Guid("5b140239-b376-2e3c-209e-b7d7f23a4048"), "POST", 24, "/api/catalog/admin/promotions/{id}/publish", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("5b1654cc-0d27-44b0-3b1c-aaefbf6b9415"), "PUT", 9, "/api/catalog/admin/categories/{id}/name", new Guid("d5439cba-7a5f-3842-8c57-7cbadd0c6d17") },
                    { new Guid("5d2d716c-7213-9001-d675-9907556396b3"), "GET", 7, "/api/catalog/admin/categories", new Guid("0b242333-b878-086a-a324-e6508c4247b9") },
                    { new Guid("5ea7f88e-0b44-a05f-0137-33ed969a6558"), "POST", 18, "/api/catalog/admin/products/{id}/unpublish", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("63491e94-08bc-a1ee-2b48-925195d777d3"), "GET", 4, "/api/user/admin/points/{userId}/entries", new Guid("f27e0c22-bcec-7fe0-5b00-3609d5f0b5e9") },
                    { new Guid("64c42468-baed-4b6d-5d16-ddeaf5cb6487"), "POST", 25, "/api/catalog/admin/promotions/{id}/pause", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("6999d9e2-c308-0788-4d8d-3632c5905a0c"), "POST", 6, "/api/user/admin/points/{userId}/adjustments", new Guid("60d2c01e-07f1-b339-645a-39bb3abe86b7") },
                    { new Guid("7414dde7-aa77-b4b6-44e0-75faf5ff42de"), "POST", 11, "/api/catalog/admin/categories/{id}/archive", new Guid("d5439cba-7a5f-3842-8c57-7cbadd0c6d17") },
                    { new Guid("78cf034b-6bd8-c689-08c6-b49a8dd7f513"), "POST", 14, "/api/catalog/admin/products", new Guid("a5698022-263e-cb9b-1a4f-f69472759566") },
                    { new Guid("7bbe4eea-e75f-b175-7efe-38697fe607a6"), "POST", 1, "/api/Auth/users/admin/{id}/roles/{role}", new Guid("2c382e8f-e49a-3a8e-94e9-20902758e580") },
                    { new Guid("81e0aefc-5fc7-eaa0-8933-cbaf961102a6"), "GET", 20, "/api/catalog/admin/promotions", new Guid("f7036a0a-4159-432b-61da-e741cf4eec8c") },
                    { new Guid("8e6ca086-da74-2f78-7871-74703529e193"), "GET", 12, "/api/catalog/admin/products", new Guid("44842b17-01d5-3dae-85a5-72f3a98d77c2") },
                    { new Guid("8f4c071c-fa09-32f0-20e5-22a1469e3e18"), "POST", 38, "/api/inventory/admin/flash-sales/{campaignId}/close", new Guid("0d6a0333-6259-4753-f4e2-b7246a25f119") },
                    { new Guid("8fc23eed-d9a5-68c9-32d9-6637154e7c4a"), "POST", 26, "/api/catalog/admin/promotions/{id}/resume", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("99677d0f-ecfd-3c9f-d4d1-239ac096c787"), "POST", 41, "/api/order/admin/orders/{orderId}/fulfillment/delivered", new Guid("e8b2b7b5-6483-fbad-e5fd-cf0588f6c50e") },
                    { new Guid("a0d4b4be-f516-3d89-1121-4d96fe21d05d"), "POST", 16, "/api/catalog/admin/products/{id}/price", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("a81b2a41-cad2-b81d-74cd-02e1ac619a3d"), "POST", 19, "/api/catalog/admin/products/{id}/archive", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("aa869063-68cc-a30c-1649-9b6561d81a92"), "GET", 3, "/api/user/admin/points/{userId}", new Guid("f27e0c22-bcec-7fe0-5b00-3609d5f0b5e9") },
                    { new Guid("b6788d56-d574-addc-ca2d-c701df97cfe4"), "POST", 2, "/api/Auth/users/admin/{id}/activation", new Guid("2c382e8f-e49a-3a8e-94e9-20902758e580") },
                    { new Guid("b87e55cd-fc53-fa3e-d209-ffc4d27b77f8"), "PUT", 15, "/api/catalog/admin/products/{id}/details", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") },
                    { new Guid("c2d092f5-bde3-dffd-dd65-435027e82203"), "POST", 34, "/api/inventory/admin/stock", new Guid("5f0db1cc-9fc4-bb4d-8bbd-e20bb40e7dd2") },
                    { new Guid("d0c6bd25-49d8-25eb-9aa8-c8779e523ef7"), "POST", 27, "/api/catalog/admin/promotions/{id}/end", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("d32dbf72-3b3d-2229-6455-037324f3e7a2"), "PUT", 23, "/api/catalog/admin/promotions/{id}", new Guid("38f4dc5b-928b-ab91-6cca-2b733e22b265") },
                    { new Guid("dd6dc230-b895-7664-b67b-feb503b3e94f"), "GET", 13, "/api/catalog/admin/products/{id}", new Guid("44842b17-01d5-3dae-85a5-72f3a98d77c2") },
                    { new Guid("de1325b4-e961-c46e-b2b3-db495b05e9f2"), "POST", 37, "/api/inventory/admin/flash-sales", new Guid("c1fe844f-3cc4-522b-6e10-4ca26d5bce09") },
                    { new Guid("e1118a3e-5f11-fbff-f443-6175af8ca8f2"), "POST", 10, "/api/catalog/admin/categories/{id}/activate", new Guid("d5439cba-7a5f-3842-8c57-7cbadd0c6d17") },
                    { new Guid("e733f179-f065-0bd9-0936-cfde450c1376"), "POST", 32, "/api/catalog/admin/coupons/{id}/disable", new Guid("83630e28-746d-3387-ea54-046054e22e88") },
                    { new Guid("f8f90f47-82c7-cf6b-09cc-3890efd7d2b1"), "GET", 0, "/api/Auth/users/admin", new Guid("b7379cac-a286-03dd-d7fc-3a2d65837018") },
                    { new Guid("fb6f2354-bee2-b3a9-1284-9b263174c944"), "GET", 28, "/api/catalog/admin/coupons", new Guid("2deb8aec-1e0d-6491-1079-b35bd9888447") },
                    { new Guid("fe57ec75-aa03-a165-a74e-ba650695f803"), "POST", 17, "/api/catalog/admin/products/{id}/publish", new Guid("0e1aee9a-52a8-3a64-6a42-d6df748d65ad") }
                });

            migrationBuilder.CreateIndex(
                name: "ux_bm_roles_normalized_code",
                schema: "bff_bm",
                table: "bm_roles",
                column: "normalized_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bm_user_roles_role_id",
                schema: "bff_bm",
                table: "bm_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictApplications_ClientId",
                schema: "bff_bm",
                table: "OpenIddictApplications",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
                schema: "bff_bm",
                table: "OpenIddictAuthorizations",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictScopes_Name",
                schema: "bff_bm",
                table: "OpenIddictScopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
                schema: "bff_bm",
                table: "OpenIddictTokens",
                columns: new[] { "ApplicationId", "Status", "Subject", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_AuthorizationId",
                schema: "bff_bm",
                table: "OpenIddictTokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenIddictTokens_ReferenceId",
                schema: "bff_bm",
                table: "OpenIddictTokens",
                column: "ReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbac_permissions_code",
                schema: "bff_bm",
                table: "pbac_permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbac_permissions_module_action",
                schema: "bff_bm",
                table: "pbac_permissions",
                columns: new[] { "module", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_pbac_policies_normalized_name",
                schema: "bff_bm",
                table: "pbac_policies",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pbac_policy_permissions_permission_id",
                schema: "bff_bm",
                table: "pbac_policy_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_pbac_role_policies_role_id",
                schema: "bff_bm",
                table: "pbac_role_policies",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_pbac_route_permissions_permission_id",
                schema: "bff_bm",
                table: "pbac_route_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ux_pbac_route_permissions_method_path",
                schema: "bff_bm",
                table: "pbac_route_permissions",
                columns: new[] { "http_method", "path_template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pbac_user_policies_user_id",
                schema: "bff_bm",
                table: "pbac_user_policies",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bm_user_roles",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "OpenIddictScopes",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "OpenIddictTokens",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_policy_permissions",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_role_policies",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_route_permissions",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_user_policies",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "OpenIddictAuthorizations",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "bm_roles",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_permissions",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "bm_users",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "pbac_policies",
                schema: "bff_bm");

            migrationBuilder.DropTable(
                name: "OpenIddictApplications",
                schema: "bff_bm");
        }
    }
}
