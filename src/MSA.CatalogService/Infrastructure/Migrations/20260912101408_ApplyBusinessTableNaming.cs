using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_coupon_quota_buckets_coupons_CouponId",
                table: "coupon_quota_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_coupons_promotions_PromotionId",
                table: "coupons");

            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_promotion_benefits_promotions_PromotionId",
                table: "promotion_benefits");

            migrationBuilder.DropForeignKey(
                name: "FK_promotion_rules_promotions_PromotionId",
                table: "promotion_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_promotion_targets_promotions_PromotionId",
                table: "promotion_targets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_promotions",
                table: "promotions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_promotion_targets",
                table: "promotion_targets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_targets_schema_version",
                table: "promotion_targets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_targets_sequence",
                table: "promotion_targets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_promotion_rules",
                table: "promotion_rules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_rules_schema_version",
                table: "promotion_rules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_rules_sequence",
                table: "promotion_rules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_promotion_benefits",
                table: "promotion_benefits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_benefits_schema_version",
                table: "promotion_benefits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_benefits_sequence",
                table: "promotion_benefits");

            migrationBuilder.DropPrimaryKey(
                name: "PK_products",
                table: "products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_coupons",
                table: "coupons");

            migrationBuilder.DropPrimaryKey(
                name: "PK_coupon_quota_buckets",
                table: "coupon_quota_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_categories",
                table: "categories");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "catalog_saga_states");

            migrationBuilder.RenameTable(
                name: "promotions",
                newName: "catalog_promotions");

            migrationBuilder.RenameTable(
                name: "promotion_targets",
                newName: "catalog_promotion_targets");

            migrationBuilder.RenameTable(
                name: "promotion_rules",
                newName: "catalog_promotion_rules");

            migrationBuilder.RenameTable(
                name: "promotion_benefits",
                newName: "catalog_promotion_benefits");

            migrationBuilder.RenameTable(
                name: "products",
                newName: "catalog_products");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "catalog_outbox_messages");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "catalog_inbox_messages");

            migrationBuilder.RenameTable(
                name: "coupons",
                newName: "catalog_coupons");

            migrationBuilder.RenameTable(
                name: "coupon_quota_buckets",
                newName: "catalog_coupon_quota_buckets");

            migrationBuilder.RenameTable(
                name: "categories",
                newName: "catalog_categories");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "catalog_saga_states",
                newName: "IX_catalog_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilU~");

            migrationBuilder.RenameIndex(
                name: "IX_promotions_Status_StartsOnUtc_EndsOnUtc",
                table: "catalog_promotions",
                newName: "IX_catalog_promotions_Status_StartsOnUtc_EndsOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_promotions_NormalizedCode",
                table: "catalog_promotions",
                newName: "IX_catalog_promotions_NormalizedCode");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_targets_TargetType_TargetKey_IsExcluded",
                table: "catalog_promotion_targets",
                newName: "IX_catalog_promotion_targets_TargetType_TargetKey_IsExcluded");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_targets_PromotionId_Sequence",
                table: "catalog_promotion_targets",
                newName: "IX_catalog_promotion_targets_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_rules_RuleType_SchemaVersion",
                table: "catalog_promotion_rules",
                newName: "IX_catalog_promotion_rules_RuleType_SchemaVersion");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_rules_PromotionId_Sequence",
                table: "catalog_promotion_rules",
                newName: "IX_catalog_promotion_rules_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_benefits_PromotionId_Sequence",
                table: "catalog_promotion_benefits",
                newName: "IX_catalog_promotion_benefits_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_benefits_BenefitType_SchemaVersion",
                table: "catalog_promotion_benefits",
                newName: "IX_catalog_promotion_benefits_BenefitType_SchemaVersion");

            migrationBuilder.RenameIndex(
                name: "IX_products_Sku",
                table: "catalog_products",
                newName: "IX_catalog_products_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_products_NormalizedSku",
                table: "catalog_products",
                newName: "IX_catalog_products_NormalizedSku");

            migrationBuilder.RenameIndex(
                name: "IX_products_LifecycleStatus_EffectiveOnUtc_Id",
                table: "catalog_products",
                newName: "IX_catalog_products_LifecycleStatus_EffectiveOnUtc_Id");

            migrationBuilder.RenameIndex(
                name: "IX_products_CategoryId_LifecycleStatus_Id",
                table: "catalog_products",
                newName: "IX_catalog_products_CategoryId_LifecycleStatus_Id");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "catalog_outbox_messages",
                newName: "IX_catalog_outbox_messages_Status_NextAttemptOnUtc_LockedUntil~");

            migrationBuilder.RenameIndex(
                name: "IX_coupons_Status_ValidUntilUtc_Id",
                table: "catalog_coupons",
                newName: "IX_catalog_coupons_Status_ValidUntilUtc_Id");

            migrationBuilder.RenameIndex(
                name: "IX_coupons_PromotionId_Status_Id",
                table: "catalog_coupons",
                newName: "IX_catalog_coupons_PromotionId_Status_Id");

            migrationBuilder.RenameIndex(
                name: "IX_coupons_CodeDigest",
                table: "catalog_coupons",
                newName: "IX_catalog_coupons_CodeDigest");

            migrationBuilder.RenameIndex(
                name: "IX_categories_Status_Name_Id",
                table: "catalog_categories",
                newName: "IX_catalog_categories_Status_Name_Id");

            migrationBuilder.RenameIndex(
                name: "IX_categories_NormalizedCode",
                table: "catalog_categories",
                newName: "IX_catalog_categories_NormalizedCode");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_saga_states",
                table: "catalog_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_promotions",
                table: "catalog_promotions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_promotion_targets",
                table: "catalog_promotion_targets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_promotion_rules",
                table: "catalog_promotion_rules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_promotion_benefits",
                table: "catalog_promotion_benefits",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_products",
                table: "catalog_products",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_outbox_messages",
                table: "catalog_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_inbox_messages",
                table: "catalog_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_coupons",
                table: "catalog_coupons",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_coupon_quota_buckets",
                table: "catalog_coupon_quota_buckets",
                columns: new[] { "CouponId", "BucketNumber" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_catalog_categories",
                table: "catalog_categories",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_targets_schema_version",
                table: "catalog_promotion_targets",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_targets_sequence",
                table: "catalog_promotion_targets",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_rules_schema_version",
                table: "catalog_promotion_rules",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_rules_sequence",
                table: "catalog_promotion_rules",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_benefits_schema_version",
                table: "catalog_promotion_benefits",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_catalog_promotion_benefits_sequence",
                table: "catalog_promotion_benefits",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_coupon_quota_buckets_catalog_coupons_CouponId",
                table: "catalog_coupon_quota_buckets",
                column: "CouponId",
                principalTable: "catalog_coupons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_coupons_catalog_promotions_PromotionId",
                table: "catalog_coupons",
                column: "PromotionId",
                principalTable: "catalog_promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_products_catalog_categories_CategoryId",
                table: "catalog_products",
                column: "CategoryId",
                principalTable: "catalog_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_promotion_benefits_catalog_promotions_PromotionId",
                table: "catalog_promotion_benefits",
                column: "PromotionId",
                principalTable: "catalog_promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_promotion_rules_catalog_promotions_PromotionId",
                table: "catalog_promotion_rules",
                column: "PromotionId",
                principalTable: "catalog_promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_promotion_targets_catalog_promotions_PromotionId",
                table: "catalog_promotion_targets",
                column: "PromotionId",
                principalTable: "catalog_promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_catalog_coupon_quota_buckets_catalog_coupons_CouponId",
                table: "catalog_coupon_quota_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_coupons_catalog_promotions_PromotionId",
                table: "catalog_coupons");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_products_catalog_categories_CategoryId",
                table: "catalog_products");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_promotion_benefits_catalog_promotions_PromotionId",
                table: "catalog_promotion_benefits");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_promotion_rules_catalog_promotions_PromotionId",
                table: "catalog_promotion_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_catalog_promotion_targets_catalog_promotions_PromotionId",
                table: "catalog_promotion_targets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_saga_states",
                table: "catalog_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_promotions",
                table: "catalog_promotions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_promotion_targets",
                table: "catalog_promotion_targets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_targets_schema_version",
                table: "catalog_promotion_targets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_targets_sequence",
                table: "catalog_promotion_targets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_promotion_rules",
                table: "catalog_promotion_rules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_rules_schema_version",
                table: "catalog_promotion_rules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_rules_sequence",
                table: "catalog_promotion_rules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_promotion_benefits",
                table: "catalog_promotion_benefits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_benefits_schema_version",
                table: "catalog_promotion_benefits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_catalog_promotion_benefits_sequence",
                table: "catalog_promotion_benefits");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_products",
                table: "catalog_products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_outbox_messages",
                table: "catalog_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_inbox_messages",
                table: "catalog_inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_coupons",
                table: "catalog_coupons");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_coupon_quota_buckets",
                table: "catalog_coupon_quota_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_catalog_categories",
                table: "catalog_categories");

            migrationBuilder.RenameTable(
                name: "catalog_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "catalog_promotions",
                newName: "promotions");

            migrationBuilder.RenameTable(
                name: "catalog_promotion_targets",
                newName: "promotion_targets");

            migrationBuilder.RenameTable(
                name: "catalog_promotion_rules",
                newName: "promotion_rules");

            migrationBuilder.RenameTable(
                name: "catalog_promotion_benefits",
                newName: "promotion_benefits");

            migrationBuilder.RenameTable(
                name: "catalog_products",
                newName: "products");

            migrationBuilder.RenameTable(
                name: "catalog_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "catalog_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "catalog_coupons",
                newName: "coupons");

            migrationBuilder.RenameTable(
                name: "catalog_coupon_quota_buckets",
                newName: "coupon_quota_buckets");

            migrationBuilder.RenameTable(
                name: "catalog_categories",
                newName: "categories");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilU~",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotions_Status_StartsOnUtc_EndsOnUtc",
                table: "promotions",
                newName: "IX_promotions_Status_StartsOnUtc_EndsOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotions_NormalizedCode",
                table: "promotions",
                newName: "IX_promotions_NormalizedCode");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_targets_TargetType_TargetKey_IsExcluded",
                table: "promotion_targets",
                newName: "IX_promotion_targets_TargetType_TargetKey_IsExcluded");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_targets_PromotionId_Sequence",
                table: "promotion_targets",
                newName: "IX_promotion_targets_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_rules_RuleType_SchemaVersion",
                table: "promotion_rules",
                newName: "IX_promotion_rules_RuleType_SchemaVersion");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_rules_PromotionId_Sequence",
                table: "promotion_rules",
                newName: "IX_promotion_rules_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_benefits_PromotionId_Sequence",
                table: "promotion_benefits",
                newName: "IX_promotion_benefits_PromotionId_Sequence");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_promotion_benefits_BenefitType_SchemaVersion",
                table: "promotion_benefits",
                newName: "IX_promotion_benefits_BenefitType_SchemaVersion");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_Sku",
                table: "products",
                newName: "IX_products_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_NormalizedSku",
                table: "products",
                newName: "IX_products_NormalizedSku");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_LifecycleStatus_EffectiveOnUtc_Id",
                table: "products",
                newName: "IX_products_LifecycleStatus_EffectiveOnUtc_Id");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_products_CategoryId_LifecycleStatus_Id",
                table: "products",
                newName: "IX_products_CategoryId_LifecycleStatus_Id");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_outbox_messages_Status_NextAttemptOnUtc_LockedUntil~",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_coupons_Status_ValidUntilUtc_Id",
                table: "coupons",
                newName: "IX_coupons_Status_ValidUntilUtc_Id");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_coupons_PromotionId_Status_Id",
                table: "coupons",
                newName: "IX_coupons_PromotionId_Status_Id");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_coupons_CodeDigest",
                table: "coupons",
                newName: "IX_coupons_CodeDigest");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_categories_Status_Name_Id",
                table: "categories",
                newName: "IX_categories_Status_Name_Id");

            migrationBuilder.RenameIndex(
                name: "IX_catalog_categories_NormalizedCode",
                table: "categories",
                newName: "IX_categories_NormalizedCode");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_promotions",
                table: "promotions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_promotion_targets",
                table: "promotion_targets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_promotion_rules",
                table: "promotion_rules",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_promotion_benefits",
                table: "promotion_benefits",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_products",
                table: "products",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_coupons",
                table: "coupons",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_coupon_quota_buckets",
                table: "coupon_quota_buckets",
                columns: new[] { "CouponId", "BucketNumber" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_categories",
                table: "categories",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_targets_schema_version",
                table: "promotion_targets",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_targets_sequence",
                table: "promotion_targets",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_rules_schema_version",
                table: "promotion_rules",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_rules_sequence",
                table: "promotion_rules",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_benefits_schema_version",
                table: "promotion_benefits",
                sql: "\"SchemaVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_benefits_sequence",
                table: "promotion_benefits",
                sql: "\"Sequence\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_coupon_quota_buckets_coupons_CouponId",
                table: "coupon_quota_buckets",
                column: "CouponId",
                principalTable: "coupons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_coupons_promotions_PromotionId",
                table: "coupons",
                column: "PromotionId",
                principalTable: "promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_benefits_promotions_PromotionId",
                table: "promotion_benefits",
                column: "PromotionId",
                principalTable: "promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_rules_promotions_PromotionId",
                table: "promotion_rules",
                column: "PromotionId",
                principalTable: "promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_promotion_targets_promotions_PromotionId",
                table: "promotion_targets",
                column: "PromotionId",
                principalTable: "promotions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
