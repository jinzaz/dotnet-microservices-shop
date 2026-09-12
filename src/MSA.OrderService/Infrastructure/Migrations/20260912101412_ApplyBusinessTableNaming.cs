using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_coupon_quota_buckets_coupon_projections_CouponId",
                table: "coupon_quota_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_coupon_projections_CouponId",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_coupon_quota_buckets_QuotaBucketId",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_orders_OrderId",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_discounts_orders_OrderId",
                table: "order_discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_order_fulfillment_status_history_orders_OrderId",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_order_status_history_orders_OrderId",
                table: "order_status_history");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_promotion_projections",
                table: "promotion_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_product_projections",
                table: "product_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_orders",
                table: "orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flash_sale_campaign_projections",
                table: "flash_sale_campaign_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_coupon_quota_buckets",
                table: "coupon_quota_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_coupon_projections",
                table: "coupon_projections");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "order_saga_states");

            migrationBuilder.RenameTable(
                name: "promotion_projections",
                newName: "order_promotion_projections");

            migrationBuilder.RenameTable(
                name: "product_projections",
                newName: "order_product_projections");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "order_outbox_messages");

            migrationBuilder.RenameTable(
                name: "orders",
                newName: "order_orders");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "order_inbox_messages");

            migrationBuilder.RenameTable(
                name: "flash_sale_campaign_projections",
                newName: "order_flash_sale_campaign_projections");

            migrationBuilder.RenameTable(
                name: "coupon_quota_buckets",
                newName: "order_coupon_quota_buckets");

            migrationBuilder.RenameTable(
                name: "coupon_projections",
                newName: "order_coupon_projections");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "order_saga_states",
                newName: "IX_order_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_projections_Status_StartsOnUtc_EndsOnUtc_Priority",
                table: "order_promotion_projections",
                newName: "IX_order_promotion_projections_Status_StartsOnUtc_EndsOnUtc_Pr~");

            migrationBuilder.RenameIndex(
                name: "IX_promotion_projections_Code",
                table: "order_promotion_projections",
                newName: "IX_order_promotion_projections_Code");

            migrationBuilder.RenameIndex(
                name: "IX_product_projections_Sku",
                table: "order_product_projections",
                newName: "IX_order_product_projections_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_product_projections_IsActive_CategoryId",
                table: "order_product_projections",
                newName: "IX_order_product_projections_IsActive_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "order_outbox_messages",
                newName: "IX_order_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_UserId_SourceBasketId_SourceBasketVersion",
                table: "order_orders",
                newName: "IX_order_orders_UserId_SourceBasketId_SourceBasketVersion");

            migrationBuilder.RenameIndex(
                name: "IX_orders_UserId_IdempotencyKey",
                table: "order_orders",
                newName: "IX_order_orders_UserId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_orders_UserId_CreatedOnUtc",
                table: "order_orders",
                newName: "IX_order_orders_UserId_CreatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_Status_CreatedOnUtc",
                table: "order_orders",
                newName: "IX_order_orders_Status_CreatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_OrderNumber",
                table: "order_orders",
                newName: "IX_order_orders_OrderNumber");

            migrationBuilder.RenameIndex(
                name: "IX_orders_FulfillmentStatus_UpdatedOnUtc",
                table: "order_orders",
                newName: "IX_order_orders_FulfillmentStatus_UpdatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_orders_FlashSaleCampaignId_UserId",
                table: "order_orders",
                newName: "IX_order_orders_FlashSaleCampaignId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_orders_FlashSaleCampaignId_Status",
                table: "order_orders",
                newName: "IX_order_orders_FlashSaleCampaignId_Status");

            migrationBuilder.RenameIndex(
                name: "IX_flash_sale_campaign_projections_IsOpen_StartsOnUtc_EndsOnUtc",
                table: "order_flash_sale_campaign_projections",
                newName: "IX_order_flash_sale_campaign_projections_IsOpen_StartsOnUtc_En~");

            migrationBuilder.RenameIndex(
                name: "IX_coupon_quota_buckets_CouponId_BucketNumber",
                table: "order_coupon_quota_buckets",
                newName: "IX_order_coupon_quota_buckets_CouponId_BucketNumber");

            migrationBuilder.RenameIndex(
                name: "IX_coupon_projections_PromotionId_Status",
                table: "order_coupon_projections",
                newName: "IX_order_coupon_projections_PromotionId_Status");

            migrationBuilder.RenameIndex(
                name: "IX_coupon_projections_CodeDigest",
                table: "order_coupon_projections",
                newName: "IX_order_coupon_projections_CodeDigest");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_saga_states",
                table: "order_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_promotion_projections",
                table: "order_promotion_projections",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections",
                column: "ProductId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_outbox_messages",
                table: "order_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_orders",
                table: "order_orders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_inbox_messages",
                table: "order_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_flash_sale_campaign_projections",
                table: "order_flash_sale_campaign_projections",
                column: "CampaignId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_coupon_quota_buckets",
                table: "order_coupon_quota_buckets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_order_coupon_projections",
                table: "order_coupon_projections",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_quota_buckets_order_coupon_projections_CouponId",
                table: "order_coupon_quota_buckets",
                column: "CouponId",
                principalTable: "order_coupon_projections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_order_coupon_projections_CouponId",
                table: "order_coupon_redemptions",
                column: "CouponId",
                principalTable: "order_coupon_projections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_order_coupon_quota_buckets_QuotaBu~",
                table: "order_coupon_redemptions",
                column: "QuotaBucketId",
                principalTable: "order_coupon_quota_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_order_orders_OrderId",
                table: "order_coupon_redemptions",
                column: "OrderId",
                principalTable: "order_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_discounts_order_orders_OrderId",
                table: "order_discounts",
                column: "OrderId",
                principalTable: "order_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_fulfillment_status_history_order_orders_OrderId",
                table: "order_fulfillment_status_history",
                column: "OrderId",
                principalTable: "order_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_order_orders_OrderId",
                table: "order_lines",
                column: "OrderId",
                principalTable: "order_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_status_history_order_orders_OrderId",
                table: "order_status_history",
                column: "OrderId",
                principalTable: "order_orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_quota_buckets_order_coupon_projections_CouponId",
                table: "order_coupon_quota_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_order_coupon_projections_CouponId",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_order_coupon_quota_buckets_QuotaBu~",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_coupon_redemptions_order_orders_OrderId",
                table: "order_coupon_redemptions");

            migrationBuilder.DropForeignKey(
                name: "FK_order_discounts_order_orders_OrderId",
                table: "order_discounts");

            migrationBuilder.DropForeignKey(
                name: "FK_order_fulfillment_status_history_order_orders_OrderId",
                table: "order_fulfillment_status_history");

            migrationBuilder.DropForeignKey(
                name: "FK_order_lines_order_orders_OrderId",
                table: "order_lines");

            migrationBuilder.DropForeignKey(
                name: "FK_order_status_history_order_orders_OrderId",
                table: "order_status_history");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_saga_states",
                table: "order_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_promotion_projections",
                table: "order_promotion_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_product_projections",
                table: "order_product_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_outbox_messages",
                table: "order_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_orders",
                table: "order_orders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_inbox_messages",
                table: "order_inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_flash_sale_campaign_projections",
                table: "order_flash_sale_campaign_projections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_coupon_quota_buckets",
                table: "order_coupon_quota_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_order_coupon_projections",
                table: "order_coupon_projections");

            migrationBuilder.RenameTable(
                name: "order_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "order_promotion_projections",
                newName: "promotion_projections");

            migrationBuilder.RenameTable(
                name: "order_product_projections",
                newName: "product_projections");

            migrationBuilder.RenameTable(
                name: "order_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "order_orders",
                newName: "orders");

            migrationBuilder.RenameTable(
                name: "order_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "order_flash_sale_campaign_projections",
                newName: "flash_sale_campaign_projections");

            migrationBuilder.RenameTable(
                name: "order_coupon_quota_buckets",
                newName: "coupon_quota_buckets");

            migrationBuilder.RenameTable(
                name: "order_coupon_projections",
                newName: "coupon_projections");

            migrationBuilder.RenameIndex(
                name: "IX_order_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_promotion_projections_Status_StartsOnUtc_EndsOnUtc_Pr~",
                table: "promotion_projections",
                newName: "IX_promotion_projections_Status_StartsOnUtc_EndsOnUtc_Priority");

            migrationBuilder.RenameIndex(
                name: "IX_order_promotion_projections_Code",
                table: "promotion_projections",
                newName: "IX_promotion_projections_Code");

            migrationBuilder.RenameIndex(
                name: "IX_order_product_projections_Sku",
                table: "product_projections",
                newName: "IX_product_projections_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_order_product_projections_IsActive_CategoryId",
                table: "product_projections",
                newName: "IX_product_projections_IsActive_CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_order_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_UserId_SourceBasketId_SourceBasketVersion",
                table: "orders",
                newName: "IX_orders_UserId_SourceBasketId_SourceBasketVersion");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_UserId_IdempotencyKey",
                table: "orders",
                newName: "IX_orders_UserId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_UserId_CreatedOnUtc",
                table: "orders",
                newName: "IX_orders_UserId_CreatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_Status_CreatedOnUtc",
                table: "orders",
                newName: "IX_orders_Status_CreatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_OrderNumber",
                table: "orders",
                newName: "IX_orders_OrderNumber");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_FulfillmentStatus_UpdatedOnUtc",
                table: "orders",
                newName: "IX_orders_FulfillmentStatus_UpdatedOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_FlashSaleCampaignId_UserId",
                table: "orders",
                newName: "IX_orders_FlashSaleCampaignId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_order_orders_FlashSaleCampaignId_Status",
                table: "orders",
                newName: "IX_orders_FlashSaleCampaignId_Status");

            migrationBuilder.RenameIndex(
                name: "IX_order_flash_sale_campaign_projections_IsOpen_StartsOnUtc_En~",
                table: "flash_sale_campaign_projections",
                newName: "IX_flash_sale_campaign_projections_IsOpen_StartsOnUtc_EndsOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_order_coupon_quota_buckets_CouponId_BucketNumber",
                table: "coupon_quota_buckets",
                newName: "IX_coupon_quota_buckets_CouponId_BucketNumber");

            migrationBuilder.RenameIndex(
                name: "IX_order_coupon_projections_PromotionId_Status",
                table: "coupon_projections",
                newName: "IX_coupon_projections_PromotionId_Status");

            migrationBuilder.RenameIndex(
                name: "IX_order_coupon_projections_CodeDigest",
                table: "coupon_projections",
                newName: "IX_coupon_projections_CodeDigest");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_promotion_projections",
                table: "promotion_projections",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_product_projections",
                table: "product_projections",
                column: "ProductId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_orders",
                table: "orders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_flash_sale_campaign_projections",
                table: "flash_sale_campaign_projections",
                column: "CampaignId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_coupon_quota_buckets",
                table: "coupon_quota_buckets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_coupon_projections",
                table: "coupon_projections",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_coupon_quota_buckets_coupon_projections_CouponId",
                table: "coupon_quota_buckets",
                column: "CouponId",
                principalTable: "coupon_projections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_coupon_projections_CouponId",
                table: "order_coupon_redemptions",
                column: "CouponId",
                principalTable: "coupon_projections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_coupon_quota_buckets_QuotaBucketId",
                table: "order_coupon_redemptions",
                column: "QuotaBucketId",
                principalTable: "coupon_quota_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_coupon_redemptions_orders_OrderId",
                table: "order_coupon_redemptions",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_discounts_orders_OrderId",
                table: "order_discounts",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_fulfillment_status_history_orders_OrderId",
                table: "order_fulfillment_status_history",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_lines_orders_OrderId",
                table: "order_lines",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_order_status_history_orders_OrderId",
                table: "order_status_history",
                column: "OrderId",
                principalTable: "orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
