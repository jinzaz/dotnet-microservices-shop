using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "手机号是否已完成归属验证",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "用于账号安全验证的 E.164 手机号",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "identity_sms_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true, comment: "关联的 ASP.NET Core Identity 用户 Id"),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "接收短信的 E.164 手机号"),
                    Purpose = table.Column<int>(type: "integer", nullable: false, comment: "验证码用途"),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "与云厂商无关的逻辑模板键"),
                    ProtectedCode = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false, comment: "经 Data Protection 加密的验证码载荷"),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "短信云服务商适配器名称"),
                    ProviderMessageId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "短信云服务商消息 Id"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "发送与消费状态"),
                    VerificationAttempts = table.Column<int>(type: "integer", nullable: false, comment: "已失败的验证码校验次数"),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "不包含敏感信息的发送失败代码"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "验证码创建时间"),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "记录最近更新时间"),
                    ExpiresOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "验证码失效时间"),
                    SentOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "短信服务商接受消息的时间"),
                    ConsumedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "验证码成功消费时间"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_sms_messages", x => x.Id);
                    table.CheckConstraint("CK_identity_sms_messages_attempts", "\"VerificationAttempts\" >= 0");
                    table.CheckConstraint("CK_identity_sms_messages_status", "\"Status\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_identity_sms_messages_timestamps", "\"ExpiresOnUtc\" > \"CreatedOnUtc\" AND \"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.ForeignKey(
                        name: "FK_identity_sms_messages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "PhoneNumberIndex",
                table: "AspNetUsers",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL AND \"PhoneNumberConfirmed\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_identity_sms_messages_phone_purpose_created",
                table: "identity_sms_messages",
                columns: new[] { "PhoneNumber", "Purpose", "CreatedOnUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_identity_sms_messages_provider_message",
                table: "identity_sms_messages",
                columns: new[] { "Provider", "ProviderMessageId" },
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_identity_sms_messages_status_expires",
                table: "identity_sms_messages",
                columns: new[] { "Status", "ExpiresOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_sms_messages_UserId",
                table: "identity_sms_messages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_sms_messages");

            migrationBuilder.DropIndex(
                name: "PhoneNumberIndex",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "手机号是否已完成归属验证");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "用于账号安全验证的 E.164 手机号");
        }
    }
}
