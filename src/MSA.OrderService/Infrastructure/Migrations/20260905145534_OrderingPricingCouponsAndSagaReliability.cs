using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderingPricingCouponsAndSagaReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_amount",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_order_lines_OrderId",
                table: "order_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_unit_price",
                table: "order_lines");

            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM product_projections
                        WHERE length(trim("Sku")) NOT BETWEEN 1 AND 80
                           OR length("Name") > 200
                           OR trim("Currency") !~ '^[A-Za-z]{3}$')
                    THEN
                        RAISE EXCEPTION 'Ordering migration found an invalid legacy product projection.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM product_projections
                        GROUP BY upper(trim("Sku")) HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Ordering migration found duplicate projected SKUs after case normalization.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM orders
                        WHERE length(trim("UserId")) NOT BETWEEN 1 AND 200
                           OR trim("Currency") !~ '^[A-Za-z]{3}$'
                           OR length(COALESCE("CancellationReason", '')) > 1000)
                    THEN
                        RAISE EXCEPTION 'Ordering migration found legacy order text beyond the supported limits.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM orders WHERE "Amount" <= 0 OR "Status" NOT BETWEEN 0 AND 3)
                    THEN
                        RAISE EXCEPTION 'Ordering migration found a legacy order with an invalid amount or status.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM orders WHERE "Status" = 2 AND ("ReservationId" IS NULL OR "PaymentId" IS NULL))
                    THEN
                        RAISE EXCEPTION 'Ordering migration found a confirmed legacy order without reservation/payment identifiers.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM order_lines
                        WHERE "OrderId" IS NOT NULL
                        GROUP BY "OrderId", "Sku" HAVING count(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Ordering migration found duplicate SKU lines in a legacy order.';
                    END IF;
                END
                $migration$;

                DELETE FROM order_lines WHERE "OrderId" IS NULL;
                UPDATE product_projections
                SET "Sku" = upper(trim("Sku")),
                    "Currency" = upper(trim("Currency"));
                UPDATE orders
                SET "Currency" = upper(trim("Currency")),
                    "IdempotencyKey" = CASE
                        WHEN trim("IdempotencyKey") = '' THEN 'legacy:' || replace("Id"::text, '-', '')
                        ELSE trim("IdempotencyKey")
                    END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "product_projections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "product_projections",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "product_projections",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "product_projections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceRevision",
                table: "product_projections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "orders",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentDueOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotHash",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PricingSnapshotId",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalAmount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedOnUtc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "order_lines",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineAmount",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineSubtotal",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ListUnitPrice",
                table: "order_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                UPDATE orders AS ordering
                SET "OrderNumber" = 'OLEGACY' || replace(ordering."Id"::text, '-', ''),
                    "RequestFingerprint" = upper(md5('request:' || ordering."Id"::text) || md5('request-2:' || ordering."Id"::text)),
                    "PaymentMethod" = CASE
                        (SELECT saga."Data"->>'PaymentMethod'
                         FROM saga_states AS saga
                         WHERE saga."CorrelationId" = ordering."Id"
                           AND saga."SagaType" = 'OrderFulfillment'
                         LIMIT 1)
                        WHEN 'Alipay' THEN 'Alipay'
                        WHEN 'WeChatPay' THEN 'WeChatPay'
                        ELSE 'Unknown'
                    END,
                    "SubtotalAmount" = ordering."Amount",
                    "DiscountAmount" = 0,
                    "PricingSnapshotId" = ordering."Id",
                    "PricingSnapshotHash" = upper(md5('pricing:' || ordering."Id"::text) || md5('pricing-2:' || ordering."Id"::text)),
                    "PaymentDueOnUtc" = ordering."CreatedOnUtc" + interval '15 minutes',
                    "UpdatedOnUtc" = ordering."CreatedOnUtc",
                    "ConfirmedOnUtc" = CASE WHEN ordering."Status" = 2 THEN ordering."CreatedOnUtc" ELSE NULL END,
                    "CancelledOnUtc" = CASE WHEN ordering."Status" = 3 THEN ordering."CreatedOnUtc" ELSE NULL END,
                    "CancellationReason" = CASE
                        WHEN ordering."Status" = 3 THEN COALESCE(NULLIF(ordering."CancellationReason", ''), 'Migrated legacy cancellation.')
                        ELSE ordering."CancellationReason"
                    END;

                UPDATE order_lines
                SET "ListUnitPrice" = "UnitPrice",
                    "LineSubtotal" = round("UnitPrice" * "Quantity", 2),
                    "DiscountAmount" = 0,
                    "LineAmount" = round("UnitPrice" * "Quantity", 2);

                UPDATE saga_states AS saga
                SET "Data" = jsonb_build_object(
                    'SchemaVersion', 1,
                    'UserId', ordering."UserId",
                    'PaymentMethod', ordering."PaymentMethod",
                    'PricingSnapshotId', ordering."PricingSnapshotId",
                    'PricingSnapshotHash', ordering."PricingSnapshotHash",
                    'PaymentDueOnUtc', ordering."PaymentDueOnUtc",
                    'ReservationId', ordering."ReservationId",
                    'PaymentId', ordering."PaymentId",
                    'RefundRequestId', NULL,
                    'RequiresInventoryCompensation', false,
                    'InventoryCompensated', false,
                    'RequiresPaymentCompensation', false,
                    'PaymentCompensated', false,
                    'FailureReason', COALESCE(saga."Data"->>'FailureReason', saga."FailureReason"))
                FROM orders AS ordering
                WHERE saga."SagaType" = 'OrderFulfillment'
                  AND saga."CorrelationId" = ordering."Id";

                ALTER TABLE product_projections ALTER COLUMN "SourceRevision" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "IdempotencyKey" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "DiscountAmount" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "OrderNumber" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "PaymentDueOnUtc" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "PaymentMethod" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "PricingSnapshotHash" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "PricingSnapshotId" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "RequestFingerprint" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "SubtotalAmount" DROP DEFAULT;
                ALTER TABLE orders ALTER COLUMN "UpdatedOnUtc" DROP DEFAULT;
                ALTER TABLE order_lines ALTER COLUMN "DiscountAmount" DROP DEFAULT;
                ALTER TABLE order_lines ALTER COLUMN "LineAmount" DROP DEFAULT;
                ALTER TABLE order_lines ALTER COLUMN "LineSubtotal" DROP DEFAULT;
                ALTER TABLE order_lines ALTER COLUMN "ListUnitPrice" DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "coupon_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaskedCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TotalLimit = table.Column<int>(type: "integer", nullable: true),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false),
                    QuotaStrategy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_projections", x => x.Id);
                    table.CheckConstraint("CK_coupon_projections_limits", "(\"TotalLimit\" IS NULL OR \"TotalLimit\" > 0) AND \"PerUserLimit\" > 0");
                    table.CheckConstraint("CK_coupon_projections_window", "\"ValidFromUtc\" < \"ValidUntilUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "order_discounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionRevision = table.Column<long>(type: "bigint", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_discounts", x => x.Id);
                    table.CheckConstraint("CK_order_discounts_amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_order_discounts_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_history", x => x.Id);
                    table.CheckConstraint("CK_order_status_history_status", "(\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 0 AND 3) AND \"ToStatus\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_order_status_history_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO order_status_history
                    ("Id", "OrderId", "FromStatus", "ToStatus", "Reason", "Actor", "OccurredOnUtc")
                SELECT
                    md5('order-history:' || ordering."Id"::text)::uuid,
                    ordering."Id",
                    NULL,
                    ordering."Status",
                    CASE
                        WHEN ordering."Status" = 3 THEN ordering."CancellationReason"
                        ELSE 'Imported current legacy state.'
                    END,
                    'legacy-migration',
                    ordering."UpdatedOnUtc"
                FROM orders AS ordering;
                """);

            migrationBuilder.CreateTable(
                name: "promotion_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    StackPolicy = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExclusiveGroup = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RulesJson = table.Column<string>(type: "jsonb", nullable: false),
                    BenefitsJson = table.Column<string>(type: "jsonb", nullable: false),
                    TargetsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_projections", x => x.Id);
                    table.CheckConstraint("CK_promotion_projections_revision", "\"SourceRevision\" >= 0");
                    table.CheckConstraint("CK_promotion_projections_window", "\"StartsOnUtc\" < \"EndsOnUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "coupon_quota_buckets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketNumber = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false),
                    ConsumedCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_quota_buckets", x => x.Id);
                    table.CheckConstraint("CK_coupon_quota_buckets_capacity", "\"Capacity\" > 0");
                    table.CheckConstraint("CK_coupon_quota_buckets_counters", "\"ReservedCount\" >= 0 AND \"ConsumedCount\" >= 0 AND \"ReservedCount\" + \"ConsumedCount\" <= \"Capacity\"");
                    table.ForeignKey(
                        name: "FK_coupon_quota_buckets_coupon_projections_CouponId",
                        column: x => x.CouponId,
                        principalTable: "coupon_projections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_coupon_redemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QuotaBucketId = table.Column<long>(type: "bigint", nullable: true),
                    MaskedCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_coupon_redemptions", x => x.Id);
                    table.CheckConstraint("CK_order_coupon_redemptions_amount", "\"DiscountAmount\" > 0");
                    table.CheckConstraint("CK_order_coupon_redemptions_status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_order_coupon_redemptions_coupon_projections_CouponId",
                        column: x => x.CouponId,
                        principalTable: "coupon_projections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_coupon_redemptions_coupon_quota_buckets_QuotaBucketId",
                        column: x => x.QuotaBucketId,
                        principalTable: "coupon_quota_buckets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_coupon_redemptions_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_projections_IsActive_CategoryId",
                table: "product_projections",
                columns: new[] { "IsActive", "CategoryId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_product_projections_price",
                table: "product_projections",
                sql: "\"Price\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_orders_FlashSaleCampaignId_Status",
                table: "orders",
                columns: new[] { "FlashSaleCampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_OrderNumber",
                table: "orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status_CreatedOnUtc",
                table: "orders",
                columns: new[] { "Status", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId_CreatedOnUtc",
                table: "orders",
                columns: new[] { "UserId", "CreatedOnUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_amounts",
                table: "orders",
                sql: "\"SubtotalAmount\" > 0 AND \"DiscountAmount\" >= 0 AND \"Amount\" > 0 AND \"Amount\" = \"SubtotalAmount\" - \"DiscountAmount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_cancelled_fields",
                table: "orders",
                sql: "\"Status\" <> 3 OR (\"CancellationReason\" IS NOT NULL AND \"CancelledOnUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_confirmed_fields",
                table: "orders",
                sql: "\"Status\" <> 2 OR (\"ReservationId\" IS NOT NULL AND \"PaymentId\" IS NOT NULL AND \"ConfirmedOnUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_currency",
                table: "orders",
                sql: "\"Currency\" ~ '^[A-Z]{3}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_payment_due",
                table: "orders",
                sql: "\"PaymentDueOnUtc\" > \"CreatedOnUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_payment_method",
                table: "orders",
                sql: "\"PaymentMethod\" IN ('Alipay', 'WeChatPay', 'Unknown')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_snapshot_hashes",
                table: "orders",
                sql: "\"RequestFingerprint\" ~ '^[0-9A-F]{64}$' AND \"PricingSnapshotHash\" ~ '^[0-9A-F]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_status",
                table: "orders",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_OrderId_Sku",
                table: "order_lines",
                columns: new[] { "OrderId", "Sku" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines",
                sql: "\"ListUnitPrice\" >= 0 AND \"UnitPrice\" >= 0 AND \"UnitPrice\" <= \"ListUnitPrice\" AND \"LineSubtotal\" = round(\"ListUnitPrice\" * \"Quantity\", 2) AND \"DiscountAmount\" = round((\"ListUnitPrice\" - \"UnitPrice\") * \"Quantity\", 2) AND \"LineAmount\" = round(\"UnitPrice\" * \"Quantity\", 2)");

            migrationBuilder.CreateIndex(
                name: "IX_coupon_projections_CodeDigest",
                table: "coupon_projections",
                column: "CodeDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_coupon_projections_PromotionId_Status",
                table: "coupon_projections",
                columns: new[] { "PromotionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_coupon_quota_buckets_CouponId_BucketNumber",
                table: "coupon_quota_buckets",
                columns: new[] { "CouponId", "BucketNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_coupon_redemptions_CouponId_UserId_Status",
                table: "order_coupon_redemptions",
                columns: new[] { "CouponId", "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_order_coupon_redemptions_OrderId_CouponId",
                table: "order_coupon_redemptions",
                columns: new[] { "OrderId", "CouponId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_coupon_redemptions_QuotaBucketId",
                table: "order_coupon_redemptions",
                column: "QuotaBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_order_coupon_redemptions_Status_ExpiresOnUtc",
                table: "order_coupon_redemptions",
                columns: new[] { "Status", "ExpiresOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_discounts_OrderId_PromotionId_CouponId",
                table: "order_discounts",
                columns: new[] { "OrderId", "PromotionId", "CouponId" });

            migrationBuilder.CreateIndex(
                name: "IX_order_status_history_OrderId_OccurredOnUtc",
                table: "order_status_history",
                columns: new[] { "OrderId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_projections_Code",
                table: "promotion_projections",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_projections_Status_StartsOnUtc_EndsOnUtc_Priority",
                table: "promotion_projections",
                columns: new[] { "Status", "StartsOnUtc", "EndsOnUtc", "Priority" });

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines");

            migrationBuilder.DropTable(
                name: "order_coupon_redemptions");

            migrationBuilder.DropTable(
                name: "order_discounts");

            migrationBuilder.DropTable(
                name: "order_status_history");

            migrationBuilder.DropTable(
                name: "promotion_projections");

            migrationBuilder.DropTable(
                name: "coupon_quota_buckets");

            migrationBuilder.DropTable(
                name: "coupon_projections");

            migrationBuilder.DropIndex(
                name: "IX_product_projections_IsActive_CategoryId",
                table: "product_projections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_product_projections_price",
                table: "product_projections");

            migrationBuilder.DropIndex(
                name: "IX_orders_FlashSaleCampaignId_Status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_OrderNumber",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_Status_CreatedOnUtc",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_UserId_CreatedOnUtc",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_amounts",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_cancelled_fields",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_confirmed_fields",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_currency",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_payment_due",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_payment_method",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_snapshot_hashes",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_status",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_order_lines_OrderId_Sku",
                table: "order_lines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "product_projections");

            migrationBuilder.DropColumn(
                name: "SourceRevision",
                table: "product_projections");

            migrationBuilder.DropColumn(
                name: "CancelledOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ConfirmedOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentDueOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotHash",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SubtotalAmount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "LineAmount",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "LineSubtotal",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "ListUnitPrice",
                table: "order_lines");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "product_projections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "product_projections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "product_projections",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "orders",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderId",
                table: "order_lines",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_amount",
                table: "orders",
                sql: "\"Amount\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_order_lines_OrderId",
                table: "order_lines",
                column: "OrderId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_unit_price",
                table: "order_lines",
                sql: "\"UnitPrice\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id");
        }
    }
}
