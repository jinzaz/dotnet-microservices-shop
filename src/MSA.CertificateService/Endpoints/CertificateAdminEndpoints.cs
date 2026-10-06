using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Certificates;
using MSA.CertificateService.Acme;
using MSA.CertificateService.Application;
using MSA.CertificateService.Domain;
using MSA.CertificateService.Infrastructure;

namespace MSA.CertificateService.Endpoints;

public sealed record GenerateCertificateRequest(
    string Consumer,
    string Purpose,
    string? Name,
    DateTimeOffset? ActivateAt,
    bool Exportable = true,
    bool AutoRotate = true);

/// <param name="Format">pfx（PfxBase64 + Password）或 pem（CertificatePem + PrivateKeyPem，私钥加密时填 Password）。</param>
public sealed record ImportCertificateRequest(
    string Consumer,
    string Purpose,
    string? Slot,
    string? Name,
    string Format,
    string? PfxBase64,
    string? CertificatePem,
    string? PrivateKeyPem,
    string? Password,
    DateTimeOffset? ActivateAt,
    bool Exportable = true,
    bool AutoRotate = true);

public sealed record UpdateCertificateRequest(string Name, bool AutoRotate, bool Exportable);

public sealed record ActivateCertificateRequest(DateTimeOffset? ActivateAt);

public sealed record RevokeCertificateRequest(string? Reason);

public sealed record ExportPrivateKeyRequest(string Format, string? Password);

public sealed record CertificateExport(IReadOnlyList<CertificateFile> Files);

public sealed record CertificatePurposeInfo(
    string Code,
    string DisplayName,
    IReadOnlyList<string> Consumers,
    string AcceptedKeys,
    bool CanGenerate,
    double ActivationLeadMinutes,
    double RetentionHours,
    double RotateAfterDays,
    double ValidityDays);

public sealed record DnsProviderInfo(string Name, string DisplayName, bool Configured);

public sealed record CertificateCatalog(
    IReadOnlyList<string> Consumers,
    IReadOnlyList<CertificatePurposeInfo> Purposes,
    IReadOnlyList<DnsProviderInfo> DnsProviders,
    string AcmeProductionDirectory,
    string AcmeStagingDirectory,
    bool TlsDeploymentConfigured,
    double RenewalIntervalMinutes,
    DateTimeOffset? NextRenewalRunAt,
    CertificateRenewalRun? LastRenewalRun);

public static class CertificateAdminEndpoints
{
    public const string ReadPolicy = "certificate.admin.read";
    public const string WritePolicy = "certificate.admin.write";
    private const int MaxUploadLength = 64 * 1024;

    public static WebApplication MapCertificateAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/certificate/admin");
        admin.MapGet("/catalog", GetCatalog).RequireAuthorization(ReadPolicy);
        admin.MapGet("/certificates", ListCertificates).RequireAuthorization(ReadPolicy);
        admin.MapGet("/certificates/{id:guid}", (Guid id, CertificateRegistry registry, CancellationToken cancellationToken) =>
            registry.GetAsync(id, cancellationToken)).RequireAuthorization(ReadPolicy);
        admin.MapGet("/certificates/{id:guid}/export", ExportPublic).RequireAuthorization(ReadPolicy);
        admin.MapPost("/certificates/generate", Generate).RequireAuthorization(WritePolicy);
        admin.MapPost("/certificates/import", Import).RequireAuthorization(WritePolicy);
        admin.MapPut("/certificates/{id:guid}", Update).RequireAuthorization(WritePolicy);
        admin.MapPost("/certificates/{id:guid}/activate", Activate).RequireAuthorization(WritePolicy);
        admin.MapPost("/certificates/{id:guid}/revoke", Revoke).RequireAuthorization(WritePolicy);
        // 导出私钥是读操作，但敏感程度跟写一样，要求写权限；BM 侧另有单独的权限码。
        admin.MapPost("/certificates/{id:guid}/export-private", ExportPrivate).RequireAuthorization(WritePolicy);
        admin.MapGet("/audits", (Guid? certificateId, int? take, CertificateRegistry registry, CancellationToken cancellationToken) =>
            registry.ListAuditsAsync(certificateId, take ?? 50, cancellationToken)).RequireAuthorization(ReadPolicy);

        admin.MapGet("/acme/subscriptions", (AcmeSubscriptions subscriptions, CancellationToken cancellationToken) =>
            subscriptions.ListAsync(cancellationToken)).RequireAuthorization(ReadPolicy);
        admin.MapPost("/acme/subscriptions", CreateSubscription).RequireAuthorization(WritePolicy);
        admin.MapPut("/acme/subscriptions/{id:guid}", UpdateSubscription).RequireAuthorization(WritePolicy);
        admin.MapPost("/acme/subscriptions/{id:guid}/renew", RenewSubscription).RequireAuthorization(WritePolicy);
        admin.MapDelete("/acme/subscriptions/{id:guid}", DeleteSubscription).RequireAuthorization(WritePolicy);
        admin.MapPost("/renewal/run", (CertificateRenewalTrigger trigger) =>
        {
            trigger.Request();
            return Results.Accepted();
        }).RequireAuthorization(WritePolicy);

        // HTTP-01 验证：CA 经 80 端口来取，匿名。
        app.MapGet("/.well-known/acme-challenge/{token}", async (string token, CertificateDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var now = time.GetUtcNow();
            var challenge = await db.AcmeHttpChallenges.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Token == token && item.ExpiresAt > now, cancellationToken);
            return challenge is null ? Results.NotFound() : Results.Text(challenge.KeyAuthorization, "application/octet-stream");
        }).AllowAnonymous();

        return app;
    }

    private static CertificateCatalog GetCatalog(
        IEnumerable<IDnsChallengeProvider> providers,
        IOptions<AcmeOptions> acme,
        IOptions<TlsDeploymentOptions> tls,
        IOptions<CertificateRenewalOptions> renewal,
        CertificateRenewalStatus status) => new(
            CertificateConsumers.All,
            CertificatePurposes.All.Select(purpose => new CertificatePurposeInfo(
                purpose.Code,
                purpose.DisplayName,
                purpose.Consumers,
                purpose.AcceptedKeys.ToString(),
                purpose.CanGenerate,
                purpose.ActivationLead.TotalMinutes,
                purpose.Retention.TotalHours,
                purpose.RotateAfter.TotalDays,
                purpose.Validity.TotalDays)).ToList(),
            providers.Select(provider => new DnsProviderInfo(provider.Name, provider.DisplayName, provider.IsConfigured)).ToList(),
            acme.Value.ProductionDirectory,
            acme.Value.StagingDirectory,
            !string.IsNullOrWhiteSpace(tls.Value.DeployDirectory),
            renewal.Value.Interval.TotalMinutes,
            status.NextRunAt,
            status.LastRun);

    private static Task<IReadOnlyList<CertificateView>> ListCertificates(
        string? consumer,
        string? purpose,
        bool? includeInactive,
        CertificateRegistry registry,
        CancellationToken cancellationToken) =>
        registry.ListAsync(consumer, purpose, includeInactive ?? false, cancellationToken);

    private static async Task<CertificateExport> ExportPublic(
        Guid id,
        string? format,
        CertificateRegistry registry,
        ClaimsPrincipal user,
        CancellationToken cancellationToken) =>
        new(await registry.ExportAsync(id, includePrivateKey: false, format ?? "pem", null, Actor(user), cancellationToken));

    private static async Task<IResult> Generate(
        GenerateCertificateRequest request,
        CertificateRegistry registry,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var purpose = CertificatePurposes.Find(request.Purpose) ?? throw new CertificateRuleException($"未知的证书用途 {request.Purpose}。");
        var activateAt = await registry.ResolveActivateAtAsync(request.Consumer, purpose, null, request.ActivateAt, cancellationToken);
        using var material = CertificateFiles.Generate(purpose, request.Consumer, activateAt, purpose.Validity);
        var certificate = await registry.AddAsync(
            new NewCertificate(request.Consumer, request.Purpose, null, request.Name, activateAt, request.Exportable, request.AutoRotate),
            material,
            CertificateSource.Generated,
            null,
            Actor(user),
            cancellationToken);
        trigger.Request();
        return Results.Created($"/api/certificate/admin/certificates/{certificate.Id}", await registry.GetAsync(certificate.Id, cancellationToken));
    }

    private static async Task<IResult> Import(
        ImportCertificateRequest request,
        CertificateRegistry registry,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        using var material = request.Format switch
        {
            "pfx" => CertificateFiles.FromPfx(DecodeUpload(request.PfxBase64), request.Password),
            "pem" => CertificateFiles.FromPem(
                Limit(request.CertificatePem, "证书 PEM"),
                Limit(request.PrivateKeyPem, "私钥 PEM"),
                request.Password),
            _ => throw new CertificateRuleException("导入格式只能是 pfx 或 pem。")
        };
        var certificate = await registry.AddAsync(
            new NewCertificate(request.Consumer, request.Purpose, request.Slot, request.Name, request.ActivateAt, request.Exportable, request.AutoRotate),
            material,
            CertificateSource.Imported,
            null,
            Actor(user),
            cancellationToken);
        trigger.Request();
        return Results.Created($"/api/certificate/admin/certificates/{certificate.Id}", await registry.GetAsync(certificate.Id, cancellationToken));
    }

    private static Task<CertificateView> Update(
        Guid id,
        UpdateCertificateRequest request,
        CertificateRegistry registry,
        ClaimsPrincipal user,
        CancellationToken cancellationToken) =>
        registry.UpdateAsync(id, request.Name, request.AutoRotate, request.Exportable, Actor(user), cancellationToken);

    private static async Task<CertificateView> Activate(
        Guid id,
        ActivateCertificateRequest request,
        CertificateRegistry registry,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var view = await registry.ScheduleAsync(id, request.ActivateAt, Actor(user), cancellationToken);
        trigger.Request();
        return view;
    }

    private static async Task<IResult> Revoke(
        Guid id,
        RevokeCertificateRequest request,
        CertificateRegistry registry,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await registry.RevokeAsync(id, request.Reason, Actor(user), cancellationToken);
        trigger.Request();
        return Results.NoContent();
    }

    private static async Task<CertificateExport> ExportPrivate(
        Guid id,
        ExportPrivateKeyRequest request,
        CertificateRegistry registry,
        ClaimsPrincipal user,
        CancellationToken cancellationToken) =>
        new(await registry.ExportAsync(id, includePrivateKey: true, request.Format, request.Password, Actor(user), cancellationToken));

    private static async Task<IResult> CreateSubscription(
        AcmeSubscriptionRequest request,
        AcmeSubscriptions subscriptions,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var id = await subscriptions.CreateAsync(request, Actor(user), cancellationToken);
        trigger.Request();
        return Results.Created($"/api/certificate/admin/acme/subscriptions/{id}", new { id });
    }

    private static async Task<IResult> UpdateSubscription(
        Guid id,
        AcmeSubscriptionRequest request,
        AcmeSubscriptions subscriptions,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await subscriptions.UpdateAsync(id, request, Actor(user), cancellationToken);
        trigger.Request();
        return Results.NoContent();
    }

    private static async Task<IResult> RenewSubscription(
        Guid id,
        AcmeSubscriptions subscriptions,
        CertificateRenewalTrigger trigger,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await subscriptions.RequestRenewalAsync(id, Actor(user), cancellationToken);
        trigger.Request();
        return Results.Accepted();
    }

    private static async Task<IResult> DeleteSubscription(
        Guid id,
        AcmeSubscriptions subscriptions,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        await subscriptions.DeleteAsync(id, Actor(user), cancellationToken);
        return Results.NoContent();
    }

    /// <summary>审计里记录的操作人：BM 转发的是用户本人的访问令牌，sub 是 Identity 用户 ID。</summary>
    private static string Actor(ClaimsPrincipal user)
    {
        var subject = user.FindFirstValue("sub") ?? "unknown";
        var name = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username");
        return name is null ? subject : $"{name} ({subject})";
    }

    private static byte[] DecodeUpload(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > MaxUploadLength * 4 / 3 + 4)
            throw new CertificateRuleException("请上传不超过 64 KB 的 PFX 文件。");
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new CertificateRuleException("PFX 文件内容不是有效的 base64。", exception);
        }
    }

    private static string Limit(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) || value.Length > MaxUploadLength
            ? throw new CertificateRuleException($"{field}不能为空，且不超过 64 KB。")
            : value;
}
