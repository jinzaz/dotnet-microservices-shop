using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_stack_policy",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_status",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_trigger",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_lifecycle",
                table: "catalog_products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_product_media_type",
                table: "catalog_product_media");

            migrationBuilder.DropCheckConstraint(
                name: "CK_coupons_quota_strategy",
                table: "catalog_coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_coupons_status",
                table: "catalog_coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_coupon_campaigns_status",
                table: "catalog_coupon_campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_categories_status",
                table: "catalog_categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_brands_status",
                table: "catalog_brands");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_saga_states_Status_Enum",
                table: "catalog_saga_states",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotions_StackPolicy_Enum",
                table: "catalog_promotions",
                sql: "\"StackPolicy\" IN (0, 1)");

            // 1 是已废弃的 Scheduled，旧约束仍允许它；新约束按枚举生成不含 1，先把遗留行归为 Active(2)。
            migrationBuilder.Sql("UPDATE catalog_promotions SET \"Status\" = 2 WHERE \"Status\" = 1;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotions_Status_Enum",
                table: "catalog_promotions",
                sql: "\"Status\" IN (0, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotions_TriggerType_Enum",
                table: "catalog_promotions",
                sql: "\"TriggerType\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_products_LifecycleStatus_Enum",
                table: "catalog_products",
                sql: "\"LifecycleStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_product_media_MediaType_Enum",
                table: "catalog_product_media",
                sql: "\"MediaType\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_outbox_messages_Status_Enum",
                table: "catalog_outbox_messages",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_coupons_QuotaStrategy_Enum",
                table: "catalog_coupons",
                sql: "\"QuotaStrategy\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_coupons_Status_Enum",
                table: "catalog_coupons",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_coupon_campaigns_Status_Enum",
                table: "catalog_coupon_campaigns",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_coupon_campaigns_ValidityMode_Enum",
                table: "catalog_coupon_campaigns",
                sql: "\"ValidityMode\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_categories_Status_Enum",
                table: "catalog_categories",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_brands_Status_Enum",
                table: "catalog_brands",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_saga_states_Status_Enum",
                table: "catalog_saga_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotions_StackPolicy_Enum",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotions_Status_Enum",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotions_TriggerType_Enum",
                table: "catalog_promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_products_LifecycleStatus_Enum",
                table: "catalog_products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_product_media_MediaType_Enum",
                table: "catalog_product_media");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_outbox_messages_Status_Enum",
                table: "catalog_outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_coupons_QuotaStrategy_Enum",
                table: "catalog_coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_coupons_Status_Enum",
                table: "catalog_coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_coupon_campaigns_Status_Enum",
                table: "catalog_coupon_campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_coupon_campaigns_ValidityMode_Enum",
                table: "catalog_coupon_campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_categories_Status_Enum",
                table: "catalog_categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_brands_Status_Enum",
                table: "catalog_brands");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_stack_policy",
                table: "catalog_promotions",
                sql: "\"StackPolicy\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_status",
                table: "catalog_promotions",
                sql: "\"Status\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_trigger",
                table: "catalog_promotions",
                sql: "\"TriggerType\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_lifecycle",
                table: "catalog_products",
                sql: "\"LifecycleStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_media_type",
                table: "catalog_product_media",
                sql: "\"MediaType\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_coupons_quota_strategy",
                table: "catalog_coupons",
                sql: "\"QuotaStrategy\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_coupons_status",
                table: "catalog_coupons",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_coupon_campaigns_status",
                table: "catalog_coupon_campaigns",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_categories_status",
                table: "catalog_categories",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_brands_status",
                table: "catalog_brands",
                sql: "\"Status\" BETWEEN 0 AND 2");
        }
    }
}
