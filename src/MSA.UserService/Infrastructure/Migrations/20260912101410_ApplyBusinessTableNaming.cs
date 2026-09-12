using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.UserService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApplyBusinessTableNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_loyalty_point_entries_loyalty_point_accounts_UserId",
                table: "loyalty_point_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_loyalty_point_entries",
                table: "loyalty_point_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_loyalty_point_accounts",
                table: "loyalty_point_accounts");

            migrationBuilder.RenameTable(
                name: "loyalty_point_entries",
                newName: "user_loyalty_point_entries");

            migrationBuilder.RenameTable(
                name: "loyalty_point_accounts",
                newName: "user_loyalty_point_accounts");

            migrationBuilder.RenameIndex(
                name: "IX_loyalty_point_entries_UserId_OccurredOnUtc",
                table: "user_loyalty_point_entries",
                newName: "IX_user_loyalty_point_entries_UserId_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_loyalty_point_entries_UserId_BusinessKey",
                table: "user_loyalty_point_entries",
                newName: "IX_user_loyalty_point_entries_UserId_BusinessKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_loyalty_point_entries",
                table: "user_loyalty_point_entries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_loyalty_point_accounts",
                table: "user_loyalty_point_accounts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_user_loyalty_point_entries_user_loyalty_point_accounts_User~",
                table: "user_loyalty_point_entries",
                column: "UserId",
                principalTable: "user_loyalty_point_accounts",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_loyalty_point_entries_user_loyalty_point_accounts_User~",
                table: "user_loyalty_point_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_loyalty_point_entries",
                table: "user_loyalty_point_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_loyalty_point_accounts",
                table: "user_loyalty_point_accounts");

            migrationBuilder.RenameTable(
                name: "user_loyalty_point_entries",
                newName: "loyalty_point_entries");

            migrationBuilder.RenameTable(
                name: "user_loyalty_point_accounts",
                newName: "loyalty_point_accounts");

            migrationBuilder.RenameIndex(
                name: "IX_user_loyalty_point_entries_UserId_OccurredOnUtc",
                table: "loyalty_point_entries",
                newName: "IX_loyalty_point_entries_UserId_OccurredOnUtc");

            migrationBuilder.RenameIndex(
                name: "IX_user_loyalty_point_entries_UserId_BusinessKey",
                table: "loyalty_point_entries",
                newName: "IX_loyalty_point_entries_UserId_BusinessKey");

            migrationBuilder.AddPrimaryKey(
                name: "PK_loyalty_point_entries",
                table: "loyalty_point_entries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_loyalty_point_accounts",
                table: "loyalty_point_accounts",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_loyalty_point_entries_loyalty_point_accounts_UserId",
                table: "loyalty_point_entries",
                column: "UserId",
                principalTable: "loyalty_point_accounts",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
