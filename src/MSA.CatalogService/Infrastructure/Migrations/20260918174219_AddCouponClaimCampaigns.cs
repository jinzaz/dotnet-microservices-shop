using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.CatalogService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponClaimCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "catalog_coupons",
                type: "uuid",
                nullable: true,
                comment: "领券活动标识；为空表示后台直接创建或批量发放");

            migrationBuilder.CreateTable(
                name: "catalog_coupon_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false, comment: "领券活动所属的凭券促销标识"),
                    Title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, comment: "领券中心展示的标题"),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "领券中心展示的补充说明"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "领券活动状态"),
                    ClaimStartsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "开始领取的 UTC 时间"),
                    ClaimEndsOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "停止领取的 UTC 时间"),
                    TotalQuantity = table.Column<int>(type: "integer", nullable: true, comment: "可领取总量；为空表示不限量"),
                    ClaimedQuantity = table.Column<int>(type: "integer", nullable: false, comment: "已领取数量，在活动行锁内递增"),
                    DailyQuantity = table.Column<int>(type: "integer", nullable: true, comment: "每日可领取总量；为空表示不限"),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false, comment: "每个用户最多可领取张数"),
                    PerUserDailyLimit = table.Column<int>(type: "integer", nullable: true, comment: "每个用户每日最多可领取张数；为空表示不限"),
                    ValidityMode = table.Column<int>(type: "integer", nullable: false, comment: "券有效期模式：固定时段或领取后 N 天"),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "固定时段模式的券生效 UTC 时间"),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "固定时段模式的券失效 UTC 时间"),
                    ValidDays = table.Column<int>(type: "integer", nullable: true, comment: "领取后有效天数"),
                    UsesPerCoupon = table.Column<int>(type: "integer", nullable: false, comment: "每张领取的券可使用次数"),
                    IsListed = table.Column<bool>(type: "boolean", nullable: false, comment: "是否在领券中心展示；否则仅凭链接领取"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "领券活动乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_coupon_campaigns", x => x.Id);
                    table.CheckConstraint("CK_coupon_campaigns_audit", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_coupon_campaigns_claimed", "\"ClaimedQuantity\" >= 0 AND (\"TotalQuantity\" IS NULL OR \"ClaimedQuantity\" <= \"TotalQuantity\")");
                    table.CheckConstraint("CK_coupon_campaigns_limits", "\"PerUserLimit\" > 0 AND \"UsesPerCoupon\" > 0 AND (\"DailyQuantity\" IS NULL OR \"DailyQuantity\" > 0) AND (\"PerUserDailyLimit\" IS NULL OR \"PerUserDailyLimit\" BETWEEN 1 AND \"PerUserLimit\")");
                    table.CheckConstraint("CK_coupon_campaigns_status", "\"Status\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_coupon_campaigns_validity", "(\"ValidityMode\" = 0 AND \"ValidFromUtc\" < \"ValidUntilUtc\" AND \"ValidDays\" IS NULL) OR (\"ValidityMode\" = 1 AND \"ValidDays\" > 0 AND \"ValidFromUtc\" IS NULL AND \"ValidUntilUtc\" IS NULL)");
                    table.CheckConstraint("CK_coupon_campaigns_window", "\"ClaimStartsOnUtc\" < \"ClaimEndsOnUtc\"");
                    table.ForeignKey(
                        name: "FK_catalog_coupon_campaigns_catalog_promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "catalog_promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "catalog_coupon_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false, comment: "领券活动标识"),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "领取用户标识"),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false, comment: "领取后生成的专属券标识"),
                    ClaimedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "领取的 UTC 时间"),
                    ClaimDay = table.Column<DateOnly>(type: "date", nullable: false, comment: "领取所在的业务日，用于每日限额")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_coupon_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalog_coupon_claims_catalog_coupon_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "catalog_coupon_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_catalog_coupon_claims_catalog_coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "catalog_coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupons_CampaignId",
                table: "catalog_coupons",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_campaigns_PromotionId_Status",
                table: "catalog_coupon_campaigns",
                columns: new[] { "PromotionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_campaigns_Status_ClaimEndsOnUtc_Id",
                table: "catalog_coupon_campaigns",
                columns: new[] { "Status", "ClaimEndsOnUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_claims_CampaignId_ClaimDay",
                table: "catalog_coupon_claims",
                columns: new[] { "CampaignId", "ClaimDay" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_claims_CampaignId_ClaimedOnUtc",
                table: "catalog_coupon_claims",
                columns: new[] { "CampaignId", "ClaimedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_claims_CampaignId_UserId_ClaimDay",
                table: "catalog_coupon_claims",
                columns: new[] { "CampaignId", "UserId", "ClaimDay" });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_coupon_claims_CouponId",
                table: "catalog_coupon_claims",
                column: "CouponId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_catalog_coupons_catalog_coupon_campaigns_CampaignId",
                table: "catalog_coupons",
                column: "CampaignId",
                principalTable: "catalog_coupon_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_catalog_coupons_catalog_coupon_campaigns_CampaignId",
                table: "catalog_coupons");

            migrationBuilder.DropTable(
                name: "catalog_coupon_claims");

            migrationBuilder.DropTable(
                name: "catalog_coupon_campaigns");

            migrationBuilder.DropIndex(
                name: "IX_catalog_coupons_CampaignId",
                table: "catalog_coupons");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "catalog_coupons");
        }
    }
}
