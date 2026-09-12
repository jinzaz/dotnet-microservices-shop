using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.BasketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_basket_coupons_baskets_BasketId",
                table: "basket_coupons");

            migrationBuilder.DropForeignKey(
                name: "FK_basket_items_baskets_BasketId",
                table: "basket_items");

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
                name: "PK_baskets",
                table: "baskets");

            migrationBuilder.RenameTable(
                name: "saga_states",
                newName: "basket_saga_states");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "basket_outbox_messages");

            migrationBuilder.RenameTable(
                name: "inbox_messages",
                newName: "basket_inbox_messages");

            migrationBuilder.RenameTable(
                name: "baskets",
                newName: "basket_baskets");

            migrationBuilder.RenameIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "basket_saga_states",
                newName: "IX_basket_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "basket_outbox_messages",
                newName: "IX_basket_outbox_messages_Status_NextAttemptOnUtc_LockedUntilU~");

            migrationBuilder.RenameIndex(
                name: "IX_baskets_UserId",
                table: "basket_baskets",
                newName: "IX_basket_baskets_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_saga_states",
                table: "basket_saga_states",
                column: "CorrelationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_outbox_messages",
                table: "basket_outbox_messages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_inbox_messages",
                table: "basket_inbox_messages",
                columns: new[] { "MessageId", "Consumer" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_basket_baskets",
                table: "basket_baskets",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_basket_coupons_basket_baskets_BasketId",
                table: "basket_coupons",
                column: "BasketId",
                principalTable: "basket_baskets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_basket_items_basket_baskets_BasketId",
                table: "basket_items",
                column: "BasketId",
                principalTable: "basket_baskets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_basket_coupons_basket_baskets_BasketId",
                table: "basket_coupons");

            migrationBuilder.DropForeignKey(
                name: "FK_basket_items_basket_baskets_BasketId",
                table: "basket_items");

            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_saga_states",
                table: "basket_saga_states");

            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_outbox_messages",
                table: "basket_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_inbox_messages",
                table: "basket_inbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_basket_baskets",
                table: "basket_baskets");

            migrationBuilder.RenameTable(
                name: "basket_saga_states",
                newName: "saga_states");

            migrationBuilder.RenameTable(
                name: "basket_outbox_messages",
                newName: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "basket_inbox_messages",
                newName: "inbox_messages");

            migrationBuilder.RenameTable(
                name: "basket_baskets",
                newName: "baskets");

            migrationBuilder.RenameIndex(
                name: "IX_basket_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "saga_states",
                newName: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_basket_outbox_messages_Status_NextAttemptOnUtc_LockedUntilU~",
                table: "outbox_messages",
                newName: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc");

            migrationBuilder.RenameIndex(
                name: "IX_basket_baskets_UserId",
                table: "baskets",
                newName: "IX_baskets_UserId");

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
                name: "PK_baskets",
                table: "baskets",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_basket_coupons_baskets_BasketId",
                table: "basket_coupons",
                column: "BasketId",
                principalTable: "baskets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_basket_items_baskets_BasketId",
                table: "basket_items",
                column: "BasketId",
                principalTable: "baskets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
