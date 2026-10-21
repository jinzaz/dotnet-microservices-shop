using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class UseEnumCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_identity_sms_messages_status",
                table: "identity_sms_messages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_identity_sms_messages_Purpose_Enum",
                table: "identity_sms_messages",
                sql: "\"Purpose\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_identity_sms_messages_Status_Enum",
                table: "identity_sms_messages",
                sql: "\"Status\" BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_identity_sms_messages_Purpose_Enum",
                table: "identity_sms_messages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_identity_sms_messages_Status_Enum",
                table: "identity_sms_messages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_identity_sms_messages_status",
                table: "identity_sms_messages",
                sql: "\"Status\" BETWEEN 0 AND 5");
        }
    }
}
