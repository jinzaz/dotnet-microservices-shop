using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Certificates;
using MSA.CertificateService.Application;
using MSA.CertificateService.Domain;
using MSA.CertificateService.Infrastructure;

namespace MSA.CertificateService.Acme;

/// <param name="Directory">production / staging，或自定义的 ACME 目录地址（https）。</param>
public sealed record AcmeSubscriptionRequest(
    string? Slot,
    string[]? Domains,
    AcmeChallengeType ChallengeType,
    string? DnsProvider,
    string? Directory,
    string? ContactEmail,
    bool Enabled = true);

public sealed record AcmeSubscriptionView(
    Guid Id,
    string Slot,
    string[] Domains,
    AcmeChallengeType ChallengeType,
    string? DnsProvider,
    string DirectoryUrl,
    string ContactEmail,
    bool Enabled,
    AcmeSubscriptionStatus Status,
    string? LastError,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset? NextAttemptAt,
    int FailureCount,
    bool RenewRequested,
    bool Running,
    Guid? CurrentCertificateId,
    DateTimeOffset? CurrentNotAfter,
    DateTimeOffset? RenewAt);

/// <summary>
/// ACME 订阅的管理与定时续订。续订时机：没有当前证书、域名变了、手动要求，或当前证书已用掉 2/3 寿命
/// （Let's Encrypt 正把有效期从 90 天缩到 45 天，按比例算而不是写死"到期前 30 天"）。
/// 失败按 1、2、4…24 小时退避重试，离过期还有很多次机会；手动"立即续订"不受退避限制。
/// </summary>
internal sealed partial class AcmeSubscriptions(
    CertificateDbContext db,
    CertificateRegistry registry,
    AcmeIssuer issuer,
    IEnumerable<IDnsChallengeProvider> dnsProviders,
    IOptions<AcmeOptions> options,
    TimeProvider time,
    ILogger<AcmeSubscriptions> logger)
{
    private const int MaxDomains = 100;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(20);

    public async Task<IReadOnlyList<AcmeSubscriptionView>> ListAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var current = await CurrentCertificatesAsync(cancellationToken);
        var subscriptions = await db.AcmeSubscriptions.AsNoTracking().OrderBy(subscription => subscription.Slot).ToListAsync(cancellationToken);
        return subscriptions.Select(subscription =>
        {
            var certificate = current.GetValueOrDefault(subscription.Slot);
            return new AcmeSubscriptionView(
                subscription.Id, subscription.Slot, subscription.Domains, subscription.ChallengeType, subscription.DnsProvider,
                subscription.DirectoryUrl, subscription.ContactEmail, subscription.Enabled, subscription.Status, subscription.LastError,
                subscription.LastAttemptAt, subscription.LastSucceededAt, subscription.NextAttemptAt, subscription.FailureCount,
                subscription.RenewRequested, subscription.LeaseUntil > now,
                certificate?.Id, certificate?.NotAfter, certificate is null ? null : RenewAt(certificate));
        }).ToList();
    }

    public async Task<Guid> CreateAsync(AcmeSubscriptionRequest request, string actor, CancellationToken cancellationToken)
    {
        var slot = CertificateRegistry.ValidateSlot(request.Slot);
        if (await db.AcmeSubscriptions.AnyAsync(subscription => subscription.Slot == slot, cancellationToken))
            throw new CertificateConflictException($"站点 {slot} 已经有 ACME 托管了。");

        var (domains, directoryUrl, email) = Validate(request);
        var now = time.GetUtcNow();
        var subscription = new AcmeSubscription
        {
            Id = Guid.CreateVersion7(),
            Slot = slot,
            Domains = domains,
            ChallengeType = request.ChallengeType,
            DnsProvider = request.ChallengeType == AcmeChallengeType.Dns01 ? request.DnsProvider : null,
            DirectoryUrl = directoryUrl,
            ContactEmail = email,
            Enabled = request.Enabled,
            Status = AcmeSubscriptionStatus.Pending,
            RenewRequested = true,
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now
        };
        db.AcmeSubscriptions.Add(subscription);
        registry.AddAudit(actor, "acme-create", null, subscription.Id, $"{slot}: {string.Join(", ", domains)}（{request.ChallengeType}）");
        await db.SaveChangesAsync(cancellationToken);
        return subscription.Id;
    }

    public async Task UpdateAsync(Guid id, AcmeSubscriptionRequest request, string actor, CancellationToken cancellationToken)
    {
        var subscription = await FindAsync(id, cancellationToken);
        var (domains, directoryUrl, email) = Validate(request);
        // 域名或 CA 变了，现有证书已经不对应了，下一轮立即重新申请。
        if (!domains.SequenceEqual(subscription.Domains) || directoryUrl != subscription.DirectoryUrl)
            subscription.RenewRequested = true;

        subscription.Domains = domains;
        subscription.ChallengeType = request.ChallengeType;
        subscription.DnsProvider = request.ChallengeType == AcmeChallengeType.Dns01 ? request.DnsProvider : null;
        subscription.DirectoryUrl = directoryUrl;
        subscription.ContactEmail = email;
        subscription.Enabled = request.Enabled;
        subscription.UpdatedAt = time.GetUtcNow();
        registry.AddAudit(actor, "acme-update", null, id, $"{string.Join(", ", domains)}（{request.ChallengeType}），启用={request.Enabled}");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>删除托管不动已签发的证书：站点继续用当前证书，只是不再自动续订。</summary>
    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var subscription = await FindAsync(id, cancellationToken);
        db.AcmeSubscriptions.Remove(subscription);
        registry.AddAudit(actor, "acme-delete", null, id, subscription.Slot);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RequestRenewalAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var subscription = await FindAsync(id, cancellationToken);
        subscription.RenewRequested = true;
        subscription.NextAttemptAt = null;
        subscription.UpdatedAt = time.GetUtcNow();
        registry.AddAudit(actor, "acme-renew-requested", null, id, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>定时任务入口：逐个处理到期的订阅。每个订阅先抢租约，多个实例不会重复申请。</summary>
    public async Task<AcmeRenewalResult> RenewDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var current = await CurrentCertificatesAsync(cancellationToken);
        var subscriptions = await db.AcmeSubscriptions.AsNoTracking()
            .Where(subscription => subscription.Enabled)
            .ToListAsync(cancellationToken);

        var renewed = 0;
        var failed = 0;
        foreach (var subscription in subscriptions)
        {
            if (!IsDue(subscription, current.GetValueOrDefault(subscription.Slot), now))
                continue;
            if (!subscription.RenewRequested && subscription.NextAttemptAt > now)
                continue;
            if (!await TryClaimAsync(subscription.Id, now, cancellationToken))
                continue;

            if (await RunAsync(subscription.Id, cancellationToken))
                renewed++;
            else
                failed++;
        }

        return new AcmeRenewalResult(renewed, failed, subscriptions.Select(subscription => subscription.Slot).ToHashSet(StringComparer.Ordinal));
    }

    internal static bool IsDue(AcmeSubscription subscription, CertificateView? current, DateTimeOffset now) =>
        subscription.RenewRequested ||
        current is null ||
        !current.DnsNames.Order(StringComparer.Ordinal).SequenceEqual(subscription.Domains.Order(StringComparer.Ordinal)) ||
        now >= RenewAt(current);

    /// <summary>用掉 2/3 寿命时续订：90 天证书剩 30 天、45 天证书剩 15 天。</summary>
    internal static DateTimeOffset RenewAt(CertificateView certificate) =>
        certificate.NotBefore + (certificate.NotAfter - certificate.NotBefore) / 3 * 2;

    private async Task<bool> RunAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = await FindAsync(id, cancellationToken);
        try
        {
            await issuer.IssueAsync(subscription, CertificateRegistry.SystemActor, cancellationToken);
            subscription.Status = AcmeSubscriptionStatus.Succeeded;
            subscription.LastSucceededAt = time.GetUtcNow();
            subscription.LastError = null;
            subscription.FailureCount = 0;
            subscription.NextAttemptAt = null;
            subscription.RenewRequested = false;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 失败时上下文里可能残留没存成功的实体，清掉后重新读订阅再记失败。
            db.ChangeTracker.Clear();
            subscription = await FindAsync(id, CancellationToken.None);
            subscription.Status = AcmeSubscriptionStatus.Failed;
            subscription.FailureCount++;
            subscription.LastError = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            subscription.NextAttemptAt = time.GetUtcNow() + Backoff(subscription.FailureCount);
            subscription.RenewRequested = false;
            registry.AddAudit(CertificateRegistry.SystemActor, "acme-failed", null, id, subscription.LastError);
            LogFailed(exception, subscription.Slot, subscription.FailureCount, subscription.NextAttemptAt.Value);
            return false;
        }
        finally
        {
            subscription.LeaseUntil = null;
            subscription.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<bool> TryClaimAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.AcmeSubscriptions
            .Where(subscription => subscription.Id == id && subscription.Enabled && (subscription.LeaseUntil == null || subscription.LeaseUntil < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(subscription => subscription.LeaseUntil, now + LeaseDuration)
                .SetProperty(subscription => subscription.LastAttemptAt, now), cancellationToken) == 1;

    private static TimeSpan Backoff(int failures) => TimeSpan.FromHours(Math.Min(24, Math.Pow(2, Math.Max(0, failures - 1))));

    private async Task<Dictionary<string, CertificateView>> CurrentCertificatesAsync(CancellationToken cancellationToken) =>
        (await registry.ListAsync(CertificateConsumers.Edge, CertificatePurposes.TlsCode, includeInactive: false, cancellationToken))
            .Where(certificate => certificate.Stage == CertificateStage.Active)
            .ToDictionary(certificate => certificate.Slot, StringComparer.Ordinal);

    private async Task<AcmeSubscription> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AcmeSubscriptions.SingleOrDefaultAsync(subscription => subscription.Id == id, cancellationToken)
        ?? throw new CertificateNotFoundException();

    private (string[] Domains, string DirectoryUrl, string Email) Validate(AcmeSubscriptionRequest request)
    {
        var domains = (request.Domains ?? [])
            .Select(domain => domain.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (domains.Length is 0 or > MaxDomains)
            throw new CertificateRuleException($"请填写 1 到 {MaxDomains} 个域名。");
        if (domains.FirstOrDefault(domain => !DomainPattern().IsMatch(domain) || domain.Length > 253) is { } invalid)
            throw new CertificateRuleException($"“{invalid}”不是有效的域名；通配符只能写在最左边，例如 *.example.com。");
        if (request.ChallengeType == AcmeChallengeType.Http01 && domains.Any(domain => domain.StartsWith("*.", StringComparison.Ordinal)))
            throw new CertificateRuleException("通配符域名只能用 DNS-01 验证（Let's Encrypt 的规定）。");

        if (request.ChallengeType == AcmeChallengeType.Dns01 &&
            !dnsProviders.Any(provider => provider.Name == request.DnsProvider && provider.IsConfigured))
        {
            throw new CertificateRuleException("DNS-01 需要选择一个已配置凭据的 DNS 服务商。");
        }

        var directoryUrl = request.Directory?.Trim() switch
        {
            null or "" or "production" => options.Value.ProductionDirectory,
            "staging" => options.Value.StagingDirectory,
            var custom when Uri.TryCreate(custom, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps => uri.AbsoluteUri,
            _ => throw new CertificateRuleException("ACME 目录只能是 production、staging 或一个 https 地址。")
        };

        if (!MailAddress.TryCreate(request.ContactEmail?.Trim(), out var email))
            throw new CertificateRuleException("请填写有效的联系邮箱（CA 用它通知账户相关事项）。");

        return (domains, directoryUrl, email.Address);
    }

    // 标签 1–63 个字符，字母数字开头结尾；通配符只允许出现在最左边一个标签。
    [GeneratedRegex(@"^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9-]{2,63}$")]
    private static partial Regex DomainPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "ACME renewal of site {Slot} failed ({Failures} in a row); retrying at {NextAttemptAt}.")]
    private partial void LogFailed(Exception exception, string slot, int failures, DateTimeOffset nextAttemptAt);
}

/// <param name="ManagedSlots">有 ACME 托管的站点，其余 TLS 站点需要人工续期，由定时任务告警。</param>
public sealed record AcmeRenewalResult(int Renewed, int Failed, IReadOnlySet<string> ManagedSlots);
