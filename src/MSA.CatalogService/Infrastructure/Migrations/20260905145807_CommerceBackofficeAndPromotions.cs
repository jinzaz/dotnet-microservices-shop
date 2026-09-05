using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CommerceBackofficeAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedSku",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UnpublishedOnUtc",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedOnUtc",
                table: "categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCode",
                table: "categories",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "categories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedOnUtc",
                table: "categories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "categories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Preserve legacy rows before required lifecycle/audit columns, foreign keys and
            // normalized unique indexes become authoritative.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM products
                        WHERE length(trim("Sku")) NOT BETWEEN 1 AND 80
                           OR trim("Currency") !~ '^[A-Za-z]{3}$')
                    THEN
                        RAISE EXCEPTION 'Catalog migration found an invalid legacy SKU or currency.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM products
                        GROUP BY upper(trim("Sku"))
                        HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Catalog migration found duplicate SKUs after case normalization.';
                    END IF;
                END
                $migration$;

                UPDATE categories
                SET "Code" = 'CAT-' || replace("Id"::text, '-', ''),
                    "NormalizedCode" = 'CAT-' || replace("Id"::text, '-', ''),
                    "Status" = 1,
                    "CreatedBy" = 'legacy-migration',
                    "UpdatedBy" = 'legacy-migration',
                    "CreatedOnUtc" = CURRENT_TIMESTAMP,
                    "UpdatedOnUtc" = CURRENT_TIMESTAMP,
                    "Version" = 1;

                INSERT INTO categories
                    ("Id", "Name", "Code", "NormalizedCode", "Status",
                     "CreatedBy", "UpdatedBy", "CreatedOnUtc", "UpdatedOnUtc", "Version")
                SELECT DISTINCT
                    product."CategoryId",
                    'Recovered legacy category',
                    'CAT-' || replace(product."CategoryId"::text, '-', ''),
                    'CAT-' || replace(product."CategoryId"::text, '-', ''),
                    1,
                    'legacy-migration',
                    'legacy-migration',
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP,
                    1
                FROM products AS product
                WHERE NOT EXISTS
                    (SELECT 1 FROM categories AS category WHERE category."Id" = product."CategoryId");

                UPDATE products
                SET "Sku" = upper(trim("Sku")),
                    "NormalizedSku" = upper(trim("Sku")),
                    "Currency" = upper(trim("Currency")),
                    "LifecycleStatus" = CASE WHEN "IsActive" THEN 1 ELSE 2 END,
                    "CreatedBy" = 'legacy-migration',
                    "UpdatedBy" = 'legacy-migration',
                    "CreatedOnUtc" = "UpdatedOnUtc",
                    "EffectiveOnUtc" = CASE WHEN "IsActive" THEN "UpdatedOnUtc" ELSE NULL END,
                    "PublishedOnUtc" = CASE WHEN "IsActive" THEN "UpdatedOnUtc" ELSE NULL END,
                    "UnpublishedOnUtc" = CASE WHEN "IsActive" THEN NULL ELSE "UpdatedOnUtc" END,
                    "Version" = GREATEST("Version", 1),
                    "Revision" = GREATEST("Version", 1);

                ALTER TABLE products ALTER COLUMN "CreatedBy" DROP DEFAULT;
                ALTER TABLE products ALTER COLUMN "CreatedOnUtc" DROP DEFAULT;
                ALTER TABLE products ALTER COLUMN "LifecycleStatus" DROP DEFAULT;
                ALTER TABLE products ALTER COLUMN "NormalizedSku" DROP DEFAULT;
                ALTER TABLE products ALTER COLUMN "Revision" DROP DEFAULT;
                ALTER TABLE products ALTER COLUMN "UpdatedBy" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "Code" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "CreatedBy" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "CreatedOnUtc" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "NormalizedCode" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "Status" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "UpdatedBy" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "UpdatedOnUtc" DROP DEFAULT;
                ALTER TABLE categories ALTER COLUMN "Version" DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "catalog_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    NormalizedCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StackPolicy = table.Column<int>(type: "integer", nullable: false),
                    ExclusiveGroup = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PublishedRevision = table.Column<long>(type: "bigint", nullable: true),
                    PublishedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DefinitionChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.Id);
                    table.CheckConstraint("CK_promotions_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_promotions_normalized_code", "\"NormalizedCode\" = upper(trim(\"Code\"))");
                    table.CheckConstraint("CK_promotions_priority", "\"Priority\" BETWEEN -1000000 AND 1000000");
                    table.CheckConstraint("CK_promotions_published_snapshot", "\"Status\" = 0 OR (\"PublishedRevision\" IS NOT NULL AND length(\"DefinitionChecksum\") = 64)");
                    table.CheckConstraint("CK_promotions_revision", "\"Revision\" > 0 AND \"Version\" > 0");
                    table.CheckConstraint("CK_promotions_stack_policy", "\"StackPolicy\" IN (0, 1)");
                    table.CheckConstraint("CK_promotions_status", "\"Status\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_promotions_trigger", "\"TriggerType\" IN (0, 1)");
                    table.CheckConstraint("CK_promotions_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaskedCode = table.Column<string>(type: "character varying(124)", maxLength: 124, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalLimit = table.Column<int>(type: "integer", nullable: true),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false),
                    QuotaStrategy = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.Id);
                    table.CheckConstraint("CK_coupons_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_coupons_code_digest", "\"CodeDigest\" ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("CK_coupons_per_user_limit", "\"PerUserLimit\" > 0 AND (\"TotalLimit\" IS NULL OR \"PerUserLimit\" <= \"TotalLimit\")");
                    table.CheckConstraint("CK_coupons_quota_strategy", "\"QuotaStrategy\" IN (0, 1)");
                    table.CheckConstraint("CK_coupons_revision", "\"Revision\" > 0 AND \"Version\" > 0");
                    table.CheckConstraint("CK_coupons_status", "\"Status\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_coupons_total_limit", "\"TotalLimit\" IS NULL OR \"TotalLimit\" > 0");
                    table.CheckConstraint("CK_coupons_window", "\"ValidFromUtc\" < \"ValidUntilUtc\"");
                    table.ForeignKey(
                        name: "FK_coupons_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_benefits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BenefitType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_benefits", x => x.Id);
                    table.CheckConstraint("CK_promotion_benefits_schema_version", "\"SchemaVersion\" > 0");
                    table.CheckConstraint("CK_promotion_benefits_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_promotion_benefits_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_rules", x => x.Id);
                    table.CheckConstraint("CK_promotion_rules_schema_version", "\"SchemaVersion\" > 0");
                    table.CheckConstraint("CK_promotion_rules_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_promotion_rules_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promotion_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsExcluded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_targets", x => x.Id);
                    table.CheckConstraint("CK_promotion_targets_schema_version", "\"SchemaVersion\" > 0");
                    table.CheckConstraint("CK_promotion_targets_sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_promotion_targets_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coupon_quota_buckets",
                columns: table => new
                {
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketNumber = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_quota_buckets", x => new { x.CouponId, x.BucketNumber });
                    table.CheckConstraint("CK_coupon_quota_buckets_capacity", "\"Capacity\" > 0");
                    table.CheckConstraint("CK_coupon_quota_buckets_number", "\"BucketNumber\" BETWEEN 0 AND 127");
                    table.ForeignKey(
                        name: "FK_coupon_quota_buckets_coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId_LifecycleStatus_Id",
                table: "products",
                columns: new[] { "CategoryId", "LifecycleStatus", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_LifecycleStatus_EffectiveOnUtc_Id",
                table: "products",
                columns: new[] { "LifecycleStatus", "EffectiveOnUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_products_NormalizedSku",
                table: "products",
                column: "NormalizedSku",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_active_lifecycle",
                table: "products",
                sql: "(\"IsActive\" AND \"LifecycleStatus\" = 1 AND \"EffectiveOnUtc\" IS NOT NULL) OR (NOT \"IsActive\" AND \"LifecycleStatus\" <> 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_audit",
                table: "products",
                sql: "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_currency",
                table: "products",
                sql: "\"Currency\" ~ '^[A-Z]{3}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_lifecycle",
                table: "products",
                sql: "\"LifecycleStatus\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_normalized_sku",
                table: "products",
                sql: "\"NormalizedSku\" = upper(trim(\"Sku\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_price",
                table: "products",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_revision",
                table: "products",
                sql: "\"Revision\" > 0 AND \"Version\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_categories_NormalizedCode",
                table: "categories",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_Status_Name_Id",
                table: "categories",
                columns: new[] { "Status", "Name", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_categories_audit",
                table: "categories",
                sql: "\"Version\" > 0 AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_categories_normalized_code",
                table: "categories",
                sql: "\"NormalizedCode\" = upper(trim(\"Code\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_categories_status",
                table: "categories",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_audit_entries_Actor_OccurredOnUtc",
                table: "catalog_audit_entries",
                columns: new[] { "Actor", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_audit_entries_AggregateType_AggregateId_OccurredOnU~",
                table: "catalog_audit_entries",
                columns: new[] { "AggregateType", "AggregateId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_coupons_CodeDigest",
                table: "coupons",
                column: "CodeDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupons_PromotionId_Status_Id",
                table: "coupons",
                columns: new[] { "PromotionId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_coupons_Status_ValidUntilUtc_Id",
                table: "coupons",
                columns: new[] { "Status", "ValidUntilUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_benefits_BenefitType_SchemaVersion",
                table: "promotion_benefits",
                columns: new[] { "BenefitType", "SchemaVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_benefits_PromotionId_Sequence",
                table: "promotion_benefits",
                columns: new[] { "PromotionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_PromotionId_Sequence",
                table: "promotion_rules",
                columns: new[] { "PromotionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_rules_RuleType_SchemaVersion",
                table: "promotion_rules",
                columns: new[] { "RuleType", "SchemaVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_targets_PromotionId_Sequence",
                table: "promotion_targets",
                columns: new[] { "PromotionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_targets_TargetType_TargetKey_IsExcluded",
                table: "promotion_targets",
                columns: new[] { "TargetType", "TargetKey", "IsExcluded" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_NormalizedCode",
                table: "promotions",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotions_Status_StartsOnUtc_EndsOnUtc",
                table: "promotions",
                columns: new[] { "Status", "StartsOnUtc", "EndsOnUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_products_categories_CategoryId",
                table: "products");

            migrationBuilder.DropTable(
                name: "catalog_audit_entries");

            migrationBuilder.DropTable(
                name: "coupon_quota_buckets");

            migrationBuilder.DropTable(
                name: "promotion_benefits");

            migrationBuilder.DropTable(
                name: "promotion_rules");

            migrationBuilder.DropTable(
                name: "promotion_targets");

            migrationBuilder.DropTable(
                name: "coupons");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_products_CategoryId_LifecycleStatus_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_LifecycleStatus_EffectiveOnUtc_Id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_NormalizedSku",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_active_lifecycle",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_audit",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_currency",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_lifecycle",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_normalized_sku",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_price",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_revision",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_categories_NormalizedCode",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_Status_Name_Id",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_categories_audit",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_categories_normalized_code",
                table: "categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_categories_status",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "ArchivedOnUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "products");

            migrationBuilder.DropColumn(
                name: "CreatedOnUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "EffectiveOnUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "products");

            migrationBuilder.DropColumn(
                name: "NormalizedSku",
                table: "products");

            migrationBuilder.DropColumn(
                name: "PublishedOnUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "products");

            migrationBuilder.DropColumn(
                name: "UnpublishedOnUtc",
                table: "products");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "products");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "CreatedOnUtc",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "NormalizedCode",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "categories");
        }
    }
}
