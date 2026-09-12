using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserBusinessTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_point_entries");

            migrationBuilder.DropTable(
                name: "user_addresses");

            migrationBuilder.DropTable(
                name: "loyalty_point_accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_point_accounts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false, comment: "积分账户所属用户标识"),
                    Balance = table.Column<long>(type: "bigint", nullable: false, comment: "当前可用积分余额"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LifetimeEarned = table.Column<long>(type: "bigint", nullable: false, comment: "账户历史累计获得积分"),
                    LifetimeSpent = table.Column<long>(type: "bigint", nullable: false, comment: "账户历史累计消费积分"),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "积分账户乐观并发版本号")
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
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false, comment: "ISO 3166-1 alpha-2 国家或地区代码"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "地址软删除的 UTC 时间"),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, comment: "是否为用户当前默认有效地址"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, comment: "地址是否可用于新订单"),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "用户自定义地址标签"),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Street = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false, comment: "地址所属用户标识"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "地址乐观并发版本号")
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
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false, comment: "积分变动后余额"),
                    BalanceBefore = table.Column<long>(type: "bigint", nullable: false, comment: "积分变动前余额"),
                    BusinessKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "积分业务幂等键"),
                    Delta = table.Column<long>(type: "bigint", nullable: false, comment: "本次积分变动量，正数为增加、负数为扣减"),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "积分变动发生的 UTC 时间"),
                    OperatorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "发起积分变动的主体标识"),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "积分变动原因"),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "积分变动来源类型"),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false, comment: "积分流水所属用户标识")
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
    }
}
