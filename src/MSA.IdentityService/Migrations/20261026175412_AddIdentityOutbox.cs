using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false, comment: "集成事件的契约类型名称"),
                    Payload = table.Column<string>(type: "jsonb", nullable: false, comment: "待发布事件的 JSON 载荷"),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "下一次允许尝试发布的 UTC 时间"),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "消息发布状态"),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, comment: "累计发布重试次数"),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true, comment: "最近一次发布失败原因"),
                    LockId = table.Column<Guid>(type: "uuid", nullable: true, comment: "当前发布任务的租约标识"),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "发布租约到期的 UTC 时间"),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_outbox_messages", x => x.Id);
                    table.CheckConstraint("CK_identity_outbox_messages_Status_Enum", "\"Status\" BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_outbox_messages_Status_NextAttemptOnUtc_LockedUnti~",
                table: "identity_outbox_messages",
                columns: new[] { "Status", "NextAttemptOnUtc", "LockedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_outbox_messages");
        }
    }
}
