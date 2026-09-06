using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSA.BasketService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBasket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "basket_product_snapshots",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_basket_product_snapshots", x => x.ProductId);
                    table.CheckConstraint("CK_basket_product_snapshots_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_basket_product_snapshots_price", "\"Price\" >= 0");
                    table.CheckConstraint("CK_basket_product_snapshots_revision", "\"SourceRevision\" >= 0");
                    table.CheckConstraint("CK_basket_product_snapshots_sku", "\"Sku\" = upper(trim(\"Sku\"))");
                });

            migrationBuilder.CreateTable(
                name: "baskets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_baskets", x => x.Id);
                    table.CheckConstraint("CK_baskets_timestamps", "\"UpdatedOnUtc\" >= \"CreatedOnUtc\"");
                    table.CheckConstraint("CK_baskets_user", "char_length(trim(\"UserId\")) > 0");
                    table.CheckConstraint("CK_baskets_version", "\"Version\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LockId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saga_states",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SagaType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeoutOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeoutLockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeoutLockId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_states", x => x.CorrelationId);
                });

            migrationBuilder.CreateTable(
                name: "basket_coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BasketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AddedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_basket_coupons", x => x.Id);
                    table.CheckConstraint("CK_basket_coupons_code", "\"Code\" = upper(trim(\"Code\"))");
                    table.ForeignKey(
                        name: "FK_basket_coupons_baskets_BasketId",
                        column: x => x.BasketId,
                        principalTable: "baskets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "basket_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BasketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductNameAtSelection = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitPriceAtSelection = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyAtSelection = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ProductRevisionAtSelection = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    AddedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_basket_items", x => x.Id);
                    table.CheckConstraint("CK_basket_items_currency", "\"CurrencyAtSelection\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_basket_items_price", "\"UnitPriceAtSelection\" >= 0");
                    table.CheckConstraint("CK_basket_items_quantity", "\"Quantity\" BETWEEN 1 AND 999");
                    table.CheckConstraint("CK_basket_items_revision", "\"ProductRevisionAtSelection\" >= 0");
                    table.CheckConstraint("CK_basket_items_sku", "\"Sku\" = upper(trim(\"Sku\"))");
                    table.CheckConstraint("CK_basket_items_timestamps", "\"UpdatedOnUtc\" >= \"AddedOnUtc\"");
                    table.ForeignKey(
                        name: "FK_basket_items_baskets_BasketId",
                        column: x => x.BasketId,
                        principalTable: "baskets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_basket_coupons_BasketId_Code",
                table: "basket_coupons",
                columns: new[] { "BasketId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_basket_items_BasketId_ProductId",
                table: "basket_items",
                columns: new[] { "BasketId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_basket_items_BasketId_Sku",
                table: "basket_items",
                columns: new[] { "BasketId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_basket_product_snapshots_IsAvailable_Sku",
                table: "basket_product_snapshots",
                columns: new[] { "IsAvailable", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_basket_product_snapshots_Sku",
                table: "basket_product_snapshots",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_baskets_UserId",
                table: "baskets",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_Status_NextAttemptOnUtc_LockedUntilUtc",
                table: "outbox_messages",
                columns: new[] { "Status", "NextAttemptOnUtc", "LockedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_saga_states_Status_TimeoutOnUtc_TimeoutLockedUntilUtc",
                table: "saga_states",
                columns: new[] { "Status", "TimeoutOnUtc", "TimeoutLockedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "basket_coupons");

            migrationBuilder.DropTable(
                name: "basket_items");

            migrationBuilder.DropTable(
                name: "basket_product_snapshots");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "saga_states");

            migrationBuilder.DropTable(
                name: "baskets");
        }
    }
}
