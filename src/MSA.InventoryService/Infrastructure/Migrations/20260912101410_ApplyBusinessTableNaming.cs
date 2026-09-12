using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_flash_sale_stock_buckets_flash_sale_campaigns_CampaignId",
                table: "flash_sale_stock_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_flash_sale_campaigns_FlashSaleCampai~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_flash_sale_stock_buckets_FlashSaleSt~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_stock_items_StockItemId",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stock_movements",
                table: "stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_stock_items",
                table: "stock_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flash_sale_stock_buckets",
                table: "flash_sale_stock_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_flash_sale_campaigns",
                table: "flash_sale_campaigns");

            migrationBuilder.RenameTable(
                name: "stock_movements",
                newName: "inventory_stock_movements");

            migrationBuilder.RenameTable(
                name: "stock_items",
                newName: "inventory_stock_items");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "inventory_saga_states");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "inventory_outbox_messages");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "inventory_inbox_messages");

            migrationBuilder.RenameTable(
                name: "flash_sale_stock_buckets",
                newName: "inventory_flash_sale_stock_buckets");

            migrationBuilder.RenameTable(
                name: "flash_sale_campaigns",
                newName: "inventory_flash_sale_campaigns");

            migrationBuilder.RenameIndex(
                name: "IX_stock_movements_Type_ReferenceId_StockItemId",
                table: "inventory_stock_movements",
                newName: "IX_inventory_stock_movements_Type_ReferenceId_StockItemId");

            migrationBuilder.RenameIndex(
                name: "IX_stock_movements_StockItemId_OccurredOnUtc",
                table: "inventory_stock_movements",
                newName: "IX_inventory_stock_movements_StockItemId_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_stock_movements_Sku_OccurredOnUtc",
                table: "inventory_stock_movements",
                newName: "IX_inventory_stock_movements_Sku_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_stock_movements_ReferenceType_ReferenceId",
                table: "inventory_stock_movements",
                newName: "IX_inventory_stock_movements_ReferenceType_ReferenceId");

            migrationBuilder.RenameIndex(
                name: "IX_stock_movements_OperatorId_IdempotencyKey",
                table: "inventory_stock_movements",
                newName: "IX_inventory_stock_movements_OperatorId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_stock_items_Sku",
                table: "inventory_stock_items",
                newName: "IX_inventory_stock_items_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "inventory_saga_states",
                newName: "IX_inventory_saga_states_Status_TimeoutOnUtc_TimeoutLockedUnti~");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "inventory_outbox_messages",
                newName: "IX_inventory_outbox_messages_Status_NextAttemptOnUtc_LockedUnt~");

            migrationBuilder.RenameIndex(
                name: "IX_flash_sale_stock_buckets_CampaignId_BucketNumber",
                table: "inventory_flash_sale_stock_buckets",
                newName: "IX_inventory_flash_sale_stock_buckets_CampaignId_BucketNumber");

            migrationBuilder.RenameIndex(
                name: "IX_flash_sale_campaigns_Status_EndsOnUtc",
                table: "inventory_flash_sale_campaigns",
                newName: "IX_inventory_flash_sale_campaigns_Status_EndsOnUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_stock_movements",
                table: "inventory_stock_movements",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_stock_items",
                table: "inventory_stock_items",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_saga_states",
                table: "inventory_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_outbox_messages",
                table: "inventory_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_inbox_messages",
                table: "inventory_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_flash_sale_stock_buckets",
                table: "inventory_flash_sale_stock_buckets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inventory_flash_sale_campaigns",
                table: "inventory_flash_sale_campaigns",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_flash_sale_stock_buckets_inventory_flash_sale_cam~",
                table: "inventory_flash_sale_stock_buckets",
                column: "CampaignId",
                principalTable: "inventory_flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_campaigns_Flash~",
                table: "inventory_reservations",
                column: "FlashSaleCampaignId",
                principalTable: "inventory_flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_stock_buckets_F~",
                table: "inventory_reservations",
                column: "FlashSaleStockBucketId",
                principalTable: "inventory_flash_sale_stock_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_stock_movements_inventory_stock_items_StockItemId",
                table: "inventory_stock_movements",
                column: "StockItemId",
                principalTable: "inventory_stock_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_flash_sale_stock_buckets_inventory_flash_sale_cam~",
                table: "inventory_flash_sale_stock_buckets");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_campaigns_Flash~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_reservations_inventory_flash_sale_stock_buckets_F~",
                table: "inventory_reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_stock_movements_inventory_stock_items_StockItemId",
                table: "inventory_stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_stock_movements",
                table: "inventory_stock_movements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_stock_items",
                table: "inventory_stock_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_saga_states",
                table: "inventory_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_outbox_messages",
                table: "inventory_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_inbox_messages",
                table: "inventory_inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_flash_sale_stock_buckets",
                table: "inventory_flash_sale_stock_buckets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_inventory_flash_sale_campaigns",
                table: "inventory_flash_sale_campaigns");

            migrationBuilder.RenameTable(
                name: "inventory_stock_movements",
                newName: "stock_movements");

            migrationBuilder.RenameTable(
                name: "inventory_stock_items",
                newName: "stock_items");

            migrationBuilder.RenameTable(
                name: "inventory_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "inventory_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "inventory_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "inventory_flash_sale_stock_buckets",
                newName: "flash_sale_stock_buckets");

            migrationBuilder.RenameTable(
                name: "inventory_flash_sale_campaigns",
                newName: "flash_sale_campaigns");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_movements_Type_ReferenceId_StockItemId",
                table: "stock_movements",
                newName: "IX_stock_movements_Type_ReferenceId_StockItemId");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_movements_StockItemId_OccurredOnUtc",
                table: "stock_movements",
                newName: "IX_stock_movements_StockItemId_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_movements_Sku_OccurredOnUtc",
                table: "stock_movements",
                newName: "IX_stock_movements_Sku_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_movements_ReferenceType_ReferenceId",
                table: "stock_movements",
                newName: "IX_stock_movements_ReferenceType_ReferenceId");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_movements_OperatorId_IdempotencyKey",
                table: "stock_movements",
                newName: "IX_stock_movements_OperatorId_IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_stock_items_Sku",
                table: "stock_items",
                newName: "IX_stock_items_Sku");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_saga_states_Status_TimeoutOnUtc_TimeoutLockedUnti~",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_outbox_messages_Status_NextAttemptOnUtc_LockedUnt~",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_flash_sale_stock_buckets_CampaignId_BucketNumber",
                table: "flash_sale_stock_buckets",
                newName: "IX_flash_sale_stock_buckets_CampaignId_BucketNumber");

            migrationBuilder.RenameIndex(
                name: "IX_inventory_flash_sale_campaigns_Status_EndsOnUtc",
                table: "flash_sale_campaigns",
                newName: "IX_flash_sale_campaigns_Status_EndsOnUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stock_movements",
                table: "stock_movements",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_stock_items",
                table: "stock_items",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saga_states",
                table: "saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_inbox_messages",
                table: "inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_flash_sale_stock_buckets",
                table: "flash_sale_stock_buckets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_flash_sale_campaigns",
                table: "flash_sale_campaigns",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_flash_sale_stock_buckets_flash_sale_campaigns_CampaignId",
                table: "flash_sale_stock_buckets",
                column: "CampaignId",
                principalTable: "flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_flash_sale_campaigns_FlashSaleCampai~",
                table: "inventory_reservations",
                column: "FlashSaleCampaignId",
                principalTable: "flash_sale_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_reservations_flash_sale_stock_buckets_FlashSaleSt~",
                table: "inventory_reservations",
                column: "FlashSaleStockBucketId",
                principalTable: "flash_sale_stock_buckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_stock_items_StockItemId",
                table: "stock_movements",
                column: "StockItemId",
                principalTable: "stock_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
