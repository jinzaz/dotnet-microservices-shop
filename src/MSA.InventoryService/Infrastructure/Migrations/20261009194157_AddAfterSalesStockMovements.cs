using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAfterSalesStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_movements_reference_type",
                table: "inventory_stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_movements_type",
                table: "inventory_stock_movements");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferenceId",
                table: "inventory_stock_movements",
                type: "uuid",
                nullable: true,
                comment: "关联的预占、秒杀活动或售后单标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "关联的预占或秒杀活动标识");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_reference_type",
                table: "inventory_stock_movements",
                sql: "\"ReferenceType\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_type",
                table: "inventory_stock_movements",
                sql: "\"Type\" BETWEEN 0 AND 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_movements_reference_type",
                table: "inventory_stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_stock_movements_type",
                table: "inventory_stock_movements");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferenceId",
                table: "inventory_stock_movements",
                type: "uuid",
                nullable: true,
                comment: "关联的预占或秒杀活动标识",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "关联的预占、秒杀活动或售后单标识");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_reference_type",
                table: "inventory_stock_movements",
                sql: "\"ReferenceType\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_type",
                table: "inventory_stock_movements",
                sql: "\"Type\" BETWEEN 0 AND 6");
        }
    }
}
