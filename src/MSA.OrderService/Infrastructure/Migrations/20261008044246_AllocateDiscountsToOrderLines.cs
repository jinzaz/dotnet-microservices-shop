using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllocateDiscountsToOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines",
                sql: "\"ListUnitPrice\" >= 0 AND \"UnitPrice\" >= 0 AND \"UnitPrice\" <= \"ListUnitPrice\" AND \"LineSubtotal\" = round(\"ListUnitPrice\" * \"Quantity\", 2) AND \"DiscountAmount\" BETWEEN 0 AND \"LineSubtotal\" AND \"LineAmount\" = \"LineSubtotal\" - \"DiscountAmount\" AND \"UnitPrice\" = round(\"LineAmount\" / \"Quantity\", 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_order_lines_prices",
                table: "order_lines",
                sql: "\"ListUnitPrice\" >= 0 AND \"UnitPrice\" >= 0 AND \"UnitPrice\" <= \"ListUnitPrice\" AND \"LineSubtotal\" = round(\"ListUnitPrice\" * \"Quantity\", 2) AND \"DiscountAmount\" = round((\"ListUnitPrice\" - \"UnitPrice\") * \"Quantity\", 2) AND \"LineAmount\" = round(\"UnitPrice\" * \"Quantity\", 2)");
        }
    }
}
