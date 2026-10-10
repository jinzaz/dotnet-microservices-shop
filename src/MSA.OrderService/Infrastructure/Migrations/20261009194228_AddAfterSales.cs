using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MSA.OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAfterSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_after_sales_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestNumber = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false, comment: "面向用户和运营的售后单号"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false, comment: "关联订单标识"),
                    OrderNumber = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false, comment: "关联订单编号快照"),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "申请售后的用户标识"),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false, comment: "原路退款使用的支付单标识"),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "用户维度的售后申请幂等键"),
                    Type = table.Column<int>(type: "integer", nullable: false, comment: "售后类型：仅退款、退货退款、换货、补寄"),
                    Status = table.Column<int>(type: "integer", nullable: false, comment: "售后状态"),
                    Reason = table.Column<int>(type: "integer", nullable: false, comment: "售后原因编码"),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "买家补充说明"),
                    EvidenceObjectKeys = table.Column<string[]>(type: "text[]", nullable: false, comment: "私有桶中凭证图片的对象键"),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "按实付折算的退款金额，换货与补寄为 0"),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "退款币种"),
                    ReturnAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "商家审核时提供的退货地址"),
                    ReturnCarrierName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, comment: "买家寄回的物流公司"),
                    ReturnTrackingNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, comment: "买家寄回的运单号"),
                    ReturnShippedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacementCarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReplacementCarrierName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, comment: "换货或补寄的物流公司"),
                    ReplacementTrackingNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, comment: "换货或补寄的运单号"),
                    ReplacementShippedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "拒绝时给买家的回复"),
                    RefundRequestId = table.Column<Guid>(type: "uuid", nullable: true, comment: "当前退款尝试的幂等标识，失败重试会换新"),
                    RefundFailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "支付渠道返回的退款失败原因"),
                    CreatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusChangedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "进入当前状态的时间，超时自动处理从此起算"),
                    ReplacementStock = table.Column<int>(type: "integer", nullable: false, comment: "换货/补寄新品的库存锁定状态"),
                    ReplacementStockFailure = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "新品库存锁定失败原因"),
                    ReturnDisposition = table.Column<int>(type: "integer", nullable: false, comment: "退回商品的入库处理结果"),
                    DispositionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "未入库原因等入库处理说明"),
                    DisposedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "入库处理操作人"),
                    DisposedOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, comment: "售后单乐观并发版本号")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_after_sales_requests", x => x.Id);
                    table.CheckConstraint("CK_after_sales_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_after_sales_refund_amount", "\"RefundAmount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "order_after_sales_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true, comment: "变更前售后状态"),
                    ToStatus = table.Column<int>(type: "integer", nullable: false, comment: "变更后售后状态"),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "给买家展示的处理说明"),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "操作人：买家、后台操作员或 system"),
                    OccurredOnUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_after_sales_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_after_sales_history_order_after_sales_requests_Reques~",
                        column: x => x.RequestId,
                        principalTable: "order_after_sales_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_after_sales_lines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "商品名称快照"),
                    VariantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "规格名称快照"),
                    Quantity = table.Column<int>(type: "integer", nullable: false, comment: "申请售后的件数"),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, comment: "该行折算的退款金额"),
                    RestockedQuantity = table.Column<int>(type: "integer", nullable: true, comment: "验货后重新入库的件数，未处理为空"),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_after_sales_lines", x => x.Id);
                    table.CheckConstraint("CK_after_sales_lines_amounts", "\"Quantity\" > 0 AND \"RefundAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_order_after_sales_lines_order_after_sales_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "order_after_sales_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_history_RequestId_OccurredOnUtc",
                table: "order_after_sales_history",
                columns: new[] { "RequestId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_lines_RequestId_Sku",
                table: "order_after_sales_lines",
                columns: new[] { "RequestId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_OrderId",
                table: "order_after_sales_requests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_RefundRequestId",
                table: "order_after_sales_requests",
                column: "RefundRequestId",
                unique: true,
                filter: "\"RefundRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_RequestNumber",
                table: "order_after_sales_requests",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_Status_StatusChangedOnUtc",
                table: "order_after_sales_requests",
                columns: new[] { "Status", "StatusChangedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_UserId_CreatedOnUtc",
                table: "order_after_sales_requests",
                columns: new[] { "UserId", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_after_sales_requests_UserId_IdempotencyKey",
                table: "order_after_sales_requests",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_after_sales_history");

            migrationBuilder.DropTable(
                name: "order_after_sales_lines");

            migrationBuilder.DropTable(
                name: "order_after_sales_requests");
        }
    }
}
