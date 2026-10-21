using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.InventoryService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_status",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_request_decisions_status",
                table: "inventory_request_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_stock_movements_ReferenceType_Enum",
                table: "inventory_stock_movements",
                sql: "\"ReferenceType\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_stock_movements_Type_Enum",
                table: "inventory_stock_movements",
                sql: "\"Type\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_saga_states_Status_Enum",
                table: "inventory_saga_states",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_Status_Enum",
                table: "inventory_reservations",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_request_decisions_Status_Enum",
                table: "inventory_request_decisions",
                sql: "\"Status\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_outbox_messages_Status_Enum",
                table: "inventory_outbox_messages",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_allocations_Status_Enum",
                table: "inventory_allocations",
                sql: "\"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_stock_movements_ReferenceType_Enum",
                table: "inventory_stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_stock_movements_Type_Enum",
                table: "inventory_stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_saga_states_Status_Enum",
                table: "inventory_saga_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_reservations_Status_Enum",
                table: "inventory_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_request_decisions_Status_Enum",
                table: "inventory_request_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_outbox_messages_Status_Enum",
                table: "inventory_outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_inventory_allocations_Status_Enum",
                table: "inventory_allocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_reference_type",
                table: "inventory_stock_movements",
                sql: "\"ReferenceType\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_stock_movements_type",
                table: "inventory_stock_movements",
                sql: "\"Type\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_reservations_status",
                table: "inventory_reservations",
                sql: "\"Status\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_inventory_request_decisions_status",
                table: "inventory_request_decisions",
                sql: "\"Status\" BETWEEN 0 AND 1");
        }
    }
}
