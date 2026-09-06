using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAddressesAndLoyaltyPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_point_accounts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Balance = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeEarned = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeSpent = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_point_accounts", x => x.UserId);
                    table.CheckConstraint("CK_loyalty_point_accounts_balances", "\"Balance\" >= 0 AND \"LifetimeEarned\" >= 0 AND \"LifetimeSpent\" >= 0 AND \"Balance\" = \"LifetimeEarned\" - \"LifetimeSpent\"");
                    table.CheckConstraint("CK_loyalty_point_accounts_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_loyalty_point_accounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_addresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Street = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_addresses", x => x.Id);
                    table.CheckConstraint("CK_user_addresses_active_default", "NOT \"IsDefault\" OR (\"IsEnabled\" AND \"DeletedOnUtc\" IS NULL)");
                    table.CheckConstraint("CK_user_addresses_country_code", "\"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_user_addresses_deletion", "\"DeletedOnUtc\" IS NULL OR NOT \"IsEnabled\"");
                    table.CheckConstraint("CK_user_addresses_version", "\"Version\" >= 0");
                    table.ForeignKey(
                        name: "FK_user_addresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_point_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    BusinessKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Delta = table.Column<long>(type: "bigint", nullable: false),
                    BalanceBefore = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_point_entries", x => x.Id);
                    table.CheckConstraint("CK_loyalty_point_entries_balances", "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0 AND \"BalanceAfter\" = \"BalanceBefore\" + \"Delta\"");
                    table.CheckConstraint("CK_loyalty_point_entries_delta", "\"Delta\" <> 0");
                    table.ForeignKey(
                        name: "FK_loyalty_point_entries_loyalty_point_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "loyalty_point_accounts",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_point_entries_UserId_BusinessKey",
                table: "loyalty_point_entries",
                columns: new[] { "UserId", "BusinessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_point_entries_UserId_OccurredOnUtc",
                table: "loyalty_point_entries",
                columns: new[] { "UserId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_addresses_UserId",
                table: "user_addresses",
                column: "UserId",
                unique: true,
                filter: "\"IsDefault\" AND \"IsEnabled\" AND \"DeletedOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_addresses_UserId_CreatedOnUtc",
                table: "user_addresses",
                columns: new[] { "UserId", "CreatedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_point_entries");

            migrationBuilder.DropTable(
                name: "user_addresses");

            migrationBuilder.DropTable(
                name: "loyalty_point_accounts");
        }
    }
}
