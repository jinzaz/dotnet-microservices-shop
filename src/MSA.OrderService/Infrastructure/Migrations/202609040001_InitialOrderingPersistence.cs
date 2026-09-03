using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable
namespace MSA.OrderService.Infrastructure.Migrations;

[DbContext(typeof(OrderDbContext))]
[Migration("202609040001_InitialOrderingPersistence")]
public sealed class InitialOrderingPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("orders", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            ProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            Quantity = table.Column<int>(type: "integer", nullable: false),
            Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
            Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
            CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_orders", x => x.Id));

        migrationBuilder.CreateTable("outbox_messages", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), EventType = table.Column<string>(type: "text", nullable: false),
            Payload = table.Column<string>(type: "jsonb", nullable: false), OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            NextAttemptOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            Status = table.Column<int>(type: "integer", nullable: false), RetryCount = table.Column<int>(type: "integer", nullable: false),
            LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true), LockId = table.Column<Guid>(type: "uuid", nullable: true),
            LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateTable("saga_states", table => new
        {
            CorrelationId = table.Column<Guid>(type: "uuid", nullable: false), SagaType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            State = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false), Data = table.Column<string>(type: "jsonb", nullable: false),
            Status = table.Column<int>(type: "integer", nullable: false), CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), TimeoutOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            TimeoutLockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), TimeoutLockId = table.Column<Guid>(type: "uuid", nullable: true),
            FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true), Version = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_saga_states", x => x.CorrelationId));

        migrationBuilder.CreateIndex("IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc", "outbox_messages", new[] { "Status", "NextAttemptOnUtc", "LockedUntilUtc" });
        migrationBuilder.CreateIndex("IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc", "saga_states", new[] { "Status", "TimeoutOnUtc", "TimeoutLockedUntilUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("orders"); migrationBuilder.DropTable("outbox_messages"); migrationBuilder.DropTable("saga_states");
    }
}
