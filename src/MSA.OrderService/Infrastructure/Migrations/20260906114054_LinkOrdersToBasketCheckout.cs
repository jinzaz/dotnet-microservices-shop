using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkOrdersToBasketCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceBasketId",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceBasketVersion",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId_SourceBasketId_SourceBasketVersion",
                table: "orders",
                columns: new[] { "UserId", "SourceBasketId", "SourceBasketVersion" },
                unique: true,
                filter: "\"SourceBasketId\" IS NOT NULL AND \"SourceBasketVersion\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_orders_source_basket_pair",
                table: "orders",
                sql: "(\"SourceBasketId\" IS NULL AND \"SourceBasketVersion\" IS NULL) OR (\"SourceBasketId\" IS NOT NULL AND \"SourceBasketVersion\" IS NOT NULL AND \"SourceBasketVersion\" >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_UserId_SourceBasketId_SourceBasketVersion",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_orders_source_basket_pair",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SourceBasketId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SourceBasketVersion",
                table: "orders");
        }
    }
}
