using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MSA.CertificateService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pki");

            migrationBuilder.CreateTable(
                name: "acme_accounts",
                schema: "pki",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    directory_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    private_key = table.Column<byte[]>(type: "bytea", nullable: false, comment: "edge KEK 加密后的 PKCS#8 账户私钥"),
                    kek_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acme_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "acme_http_challenges",
                schema: "pki",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_authorization = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acme_http_challenges", x => x.token);
                });

            migrationBuilder.CreateTable(
                name: "acme_subscriptions",
                schema: "pki",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    domains = table.Column<string[]>(type: "text[]", nullable: false),
                    challenge_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    dns_provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    directory_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "失败退避后的下一次重试时间"),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    renew_requested = table.Column<bool>(type: "boolean", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "申请进行中的租约，防止多实例重复申请"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_acme_subscriptions", x => x.id);
                },
                comment: "ACME 托管的 TLS 站点证书，定时自动续订");

            migrationBuilder.CreateTable(
                name: "certificate_audits",
                schema: "pki",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    certificate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acme_subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificate_audits", x => x.id);
                },
                comment: "证书操作审计：导入、生成、激活、吊销、导出私钥、ACME 申请");

            migrationBuilder.CreateTable(
                name: "certificates",
                schema: "pki",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "使用方：identity / bff-web / bff-bm / edge"),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "用途：oidc-signing / oidc-encryption / oidc-client / body-encryption / tls"),
                    slot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "互相替换的一组证书；TLS 为站点名"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    thumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "SHA-256 指纹"),
                    key_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dns_names = table.Column<string[]>(type: "text[]", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activate_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "成为当前证书的时间；阶段按它现算，不存状态"),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    certificate_pem = table.Column<string>(type: "text", nullable: false, comment: "证书链 PEM，叶子在前"),
                    private_key = table.Column<byte[]>(type: "bytea", nullable: false, comment: "KEK 加密后的 PKCS#8 私钥：nonce || 密文 || tag"),
                    kek_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "加密私钥所用 KEK 的指纹"),
                    exportable = table.Column<bool>(type: "boolean", nullable: false),
                    auto_rotate = table.Column<bool>(type: "boolean", nullable: false),
                    acme_subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.id);
                },
                comment: "证书库：各使用方的签名 / 加密 / TLS 证书，私钥用使用方 KEK 加密");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "pki",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    application = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xml = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                },
                comment: "各使用方的 ASP.NET Core DataProtection 密钥环，密钥用使用方 KEK 加密");

            migrationBuilder.CreateIndex(
                name: "ux_acme_accounts_directory_contact",
                schema: "pki",
                table: "acme_accounts",
                columns: new[] { "directory_url", "contact_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_acme_subscriptions_slot",
                schema: "pki",
                table: "acme_subscriptions",
                column: "slot",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificate_audits_at",
                schema: "pki",
                table: "certificate_audits",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_audits_certificate",
                schema: "pki",
                table: "certificate_audits",
                column: "certificate_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_group",
                schema: "pki",
                table: "certificates",
                columns: new[] { "consumer", "purpose", "slot" });

            migrationBuilder.CreateIndex(
                name: "ux_certificates_group_thumbprint",
                schema: "pki",
                table: "certificates",
                columns: new[] { "consumer", "purpose", "slot", "thumbprint" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_data_protection_keys_application",
                schema: "pki",
                table: "data_protection_keys",
                column: "application");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acme_accounts",
                schema: "pki");

            migrationBuilder.DropTable(
                name: "acme_http_challenges",
                schema: "pki");

            migrationBuilder.DropTable(
                name: "acme_subscriptions",
                schema: "pki");

            migrationBuilder.DropTable(
                name: "certificate_audits",
                schema: "pki");

            migrationBuilder.DropTable(
                name: "certificates",
                schema: "pki");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "pki");
        }
    }
}
