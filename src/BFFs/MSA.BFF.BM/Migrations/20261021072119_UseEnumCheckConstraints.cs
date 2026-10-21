using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.BFF.BM.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pbac_policies_effect",
                schema: "bff_bm",
                table: "pbac_policies");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pbac_permissions_action",
                schema: "bff_bm",
                table: "pbac_permissions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pbac_policies_effect_Enum",
                schema: "bff_bm",
                table: "pbac_policies",
                sql: "effect IN ('Allow', 'Deny')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_pbac_permissions_action_Enum",
                schema: "bff_bm",
                table: "pbac_permissions",
                sql: "action IN ('Query', 'Create', 'Edit', 'Delete')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_pbac_policies_effect_Enum",
                schema: "bff_bm",
                table: "pbac_policies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_pbac_permissions_action_Enum",
                schema: "bff_bm",
                table: "pbac_permissions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbac_policies_effect",
                schema: "bff_bm",
                table: "pbac_policies",
                sql: "effect IN ('Allow', 'Deny')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pbac_permissions_action",
                schema: "bff_bm",
                table: "pbac_permissions",
                sql: "action IN ('Query', 'Create', 'Edit', 'Delete')");
        }
    }
}
