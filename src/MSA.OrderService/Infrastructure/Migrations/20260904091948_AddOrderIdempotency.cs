using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE orders
                SET "IdempotencyKey" = 'legacy:' || replace("Id"::text, '-', '')
                WHERE trim("IdempotencyKey") = '';
                ALTER TABLE orders ALTER COLUMN "IdempotencyKey" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId_IdempotencyKey",
                table: "orders",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_UserId_IdempotencyKey",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "orders");
        }
    }
}
