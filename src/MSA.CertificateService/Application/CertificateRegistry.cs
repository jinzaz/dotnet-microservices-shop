using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MSA.Certificates;
using MSA.CertificateService.Domain;
using MSA.CertificateService.Infrastructure;
using MSA.Persistence.Postgres;

namespace MSA.CertificateService.Application;

public sealed record CertificateView(
    Guid Id,
    string Consumer,
    string Purpose,
    string Slot,
    string Name,
    CertificateSource Source,
    CertificateStage Stage,
    DateTimeOffset? RetireAt,
    string Subject,
    string Issuer,
    string Thumbprint,
    string KeyAlgorithm,
    string[] DnsNames,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    DateTimeOffset ActivateAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    bool Exportable,
    bool AutoRotate,
    Guid? AcmeSubscriptionId,
    DateTimeOffset CreatedAt,
    string CreatedBy);

/// <param name="ActivateAt">为空时：分组里还没有当前证书就立即生效，否则按用途的提前量排期。</param>
public sealed record NewCertificate(
    string Consumer,
    string Purpose,
    string? Slot,
    string? Name,
    DateTimeOffset? ActivateAt,
    bool Exportable,
    bool AutoRotate);

/// <summary>证书库的写入与查询。所有改动都写审计，并更新 updated_at（使用方靠它发现变化）。</summary>
public sealed partial class CertificateRegistry(
    CertificateDbContext db,
    CertificateKekRing keks,
    TimeProvider time,
    ILogger<CertificateRegistry> logger)
{
    public const string SystemActor = "system";
    private const string RotationLockKey = "msa:certificate-rotation";

    public async Task<IReadOnlyList<CertificateView>> ListAsync(
        string? consumer,
        string? purpose,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = db.Certificates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(consumer))
            query = query.Where(certificate => certificate.Consumer == consumer);
        if (!string.IsNullOrWhiteSpace(purpose))
            query = query.Where(certificate => certificate.Purpose == purpose);

        var rows = await query.Select(ViewRow).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        return rows
            .GroupBy(row => (row.Consumer, row.Purpose, row.Slot))
            .SelectMany(group =>
            {
                var states = Classify(group, now);
                return group.Select(row => ToView(row, states[row.Id]));
            })
            .Where(view => includeInactive || view.Stage is CertificateStage.Staged or CertificateStage.Active or CertificateStage.Retiring)
            .OrderBy(view => view.Consumer)
            .ThenBy(view => view.Purpose)
            .ThenBy(view => view.Slot)
            .ThenByDescending(view => view.ActivateAt)
            .ToList();
    }

    public async Task<CertificateView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var certificate = await FindAsync(id, cancellationToken);
        var group = await GroupRowsAsync(certificate, cancellationToken);
        var states = Classify(group, time.GetUtcNow());
        return ToView(group.Single(row => row.Id == id), states[id]);
    }

    public async Task<ManagedCertificate> AddAsync(
        NewCertificate input,
        CertificateMaterialSet material,
        CertificateSource source,
        Guid? acmeSubscriptionId,
        string actor,
        CancellationToken cancellationToken)
    {
        var purpose = CertificatePurposes.Find(input.Purpose)
            ?? throw new CertificateRuleException($"未知的证书用途 {input.Purpose}。");
        var slot = NormalizeSlot(purpose, input.Slot);
        var activateAt = await ResolveActivateAtAsync(input.Consumer, purpose, slot, input.ActivateAt, cancellationToken);
        var now = time.GetUtcNow();
        CertificateFiles.Validate(purpose, input.Consumer, material, activateAt);

        var thumbprint = CertificateMaterial.Thumbprint(material.Leaf);
        if (await db.Certificates.AnyAsync(certificate =>
                certificate.Consumer == input.Consumer && certificate.Purpose == purpose.Code &&
                certificate.Slot == slot && certificate.Thumbprint == thumbprint && certificate.RevokedAt == null,
                cancellationToken))
        {
            throw new CertificateConflictException("这张证书已经在证书库里了。");
        }

        var kek = keks.For(input.Consumer);
        var id = Guid.CreateVersion7();
        var certificate = new ManagedCertificate
        {
            Id = id,
            Consumer = input.Consumer,
            Purpose = purpose.Code,
            Slot = slot,
            Name = string.IsNullOrWhiteSpace(input.Name) ? $"{purpose.DisplayName} {activateAt:yyyy-MM-dd}" : input.Name.Trim(),
            Source = source,
            Subject = material.Leaf.Subject,
            Issuer = material.Leaf.Issuer,
            Thumbprint = thumbprint,
            KeyAlgorithm = CertificateMaterial.DescribeKey(material.Leaf),
            DnsNames = material.DnsNames,
            NotBefore = new DateTimeOffset(material.Leaf.NotBefore.ToUniversalTime()),
            NotAfter = new DateTimeOffset(material.Leaf.NotAfter.ToUniversalTime()),
            ActivateAt = activateAt,
            CertificatePem = material.ChainPem,
            PrivateKey = kek.Encrypt(material.PrivateKey, CertificateKek.CertificateContext(id, input.Consumer, purpose.Code)),
            KekId = kek.Id,
            Exportable = input.Exportable,
            // TLS 证书由 ACME 托管续订；导入的 TLS 证书没法自动续，只能提醒。
            AutoRotate = input.AutoRotate && purpose.CanGenerate,
            AcmeSubscriptionId = acmeSubscriptionId,
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now
        };
        db.Certificates.Add(certificate);
        AddAudit(actor, source switch { CertificateSource.Generated => "generate", CertificateSource.Acme => "acme-issue", _ => "import" }, certificate.Id, acmeSubscriptionId,
            $"{certificate.Consumer}/{certificate.Purpose}/{certificate.Slot} {certificate.KeyAlgorithm}，{activateAt:u} 生效");
        await db.SaveChangesAsync(cancellationToken);
        return certificate;
    }

    /// <summary>调整待生效证书的生效时间；不传时间即立即生效。</summary>
    public async Task<CertificateView> ScheduleAsync(Guid id, DateTimeOffset? activateAt, string actor, CancellationToken cancellationToken)
    {
        var certificate = await FindAsync(id, cancellationToken);
        var now = time.GetUtcNow();
        var states = Classify(await GroupRowsAsync(certificate, cancellationToken), now);
        if (states[id].Stage != CertificateStage.Staged)
            throw new CertificateConflictException("只有待生效的证书可以调整生效时间。");

        var at = activateAt is { } requested && requested > now ? requested : now;
        if (certificate.NotBefore > at.AddMinutes(5))
            throw new CertificateRuleException($"证书 {certificate.NotBefore:u} 才开始有效，不能早于这个时间生效。");

        certificate.ActivateAt = at;
        certificate.UpdatedAt = now;
        AddAudit(actor, "schedule", id, null, $"{at:u} 生效");
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// 吊销后证书立刻不再被加载。吊销的若是当前证书，上一张未退役的证书自动回到当前（相当于回滚）；
    /// 回不去（分组里没有别的可用证书）时拒绝，避免使用方手里一张证书都没有。
    /// </summary>
    public async Task RevokeAsync(Guid id, string? reason, string actor, CancellationToken cancellationToken)
    {
        var certificate = await FindAsync(id, cancellationToken);
        if (certificate.RevokedAt is not null)
            throw new CertificateConflictException("证书已经吊销。");

        var now = time.GetUtcNow();
        var group = await GroupRowsAsync(certificate, cancellationToken);
        var purpose = CertificatePurposes.Get(certificate.Purpose);
        if (Classify(group, now)[id].Stage == CertificateStage.Active)
        {
            var remaining = Classify(group.Where(row => row.Id != id), now);
            if (!remaining.Values.Any(state => state.Stage == CertificateStage.Active))
                throw new CertificateConflictException($"这是{purpose.DisplayName}唯一可用的证书，先导入或生成一张替代证书再吊销。");
        }

        certificate.RevokedAt = now;
        certificate.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        certificate.UpdatedAt = now;
        AddAudit(actor, "revoke", id, null, certificate.RevokedReason);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CertificateView> UpdateAsync(Guid id, string name, bool autoRotate, bool exportable, string actor, CancellationToken cancellationToken)
    {
        var certificate = await FindAsync(id, cancellationToken);
        if (exportable && !certificate.Exportable)
            throw new CertificateConflictException("私钥一旦设为不可导出就不能再改回可导出。");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new CertificateRuleException("名称不能为空，且不超过 120 个字符。");

        certificate.Name = name.Trim();
        certificate.AutoRotate = autoRotate && CertificatePurposes.Get(certificate.Purpose).CanGenerate;
        certificate.Exportable = exportable;
        certificate.UpdatedAt = time.GetUtcNow();
        AddAudit(actor, "update", id, null, $"自动轮换={certificate.AutoRotate}，可导出私钥={certificate.Exportable}");
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CertificateFile[]> ExportAsync(
        Guid id,
        bool includePrivateKey,
        string format,
        string? password,
        string actor,
        CancellationToken cancellationToken)
    {
        var certificate = await FindAsync(id, cancellationToken);
        var baseName = $"{certificate.Consumer}-{certificate.Purpose}-{certificate.Slot}-{certificate.NotBefore:yyyyMMdd}";
        if (!includePrivateKey)
            return CertificateFiles.ExportPublic(certificate.CertificatePem, baseName, format);

        if (!certificate.Exportable)
            throw new CertificateConflictException("这张证书的私钥设为不可导出。");

        var privateKey = keks.For(certificate.Consumer).Decrypt(
            certificate.PrivateKey,
            CertificateKek.CertificateContext(certificate.Id, certificate.Consumer, certificate.Purpose));
        try
        {
            var files = CertificateFiles.ExportPrivate(certificate.CertificatePem, privateKey, certificate.Purpose, baseName, format, password);
            AddAudit(actor, "export-private-key", id, null, format);
            await db.SaveChangesAsync(cancellationToken);
            LogPrivateKeyExported(id, actor);
            return files;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    public Task<List<CertificateAudit>> ListAuditsAsync(Guid? certificateId, int take, CancellationToken cancellationToken)
    {
        var query = db.Audits.AsNoTracking();
        if (certificateId is { } id)
            query = query.Where(audit => audit.CertificateId == id);
        return query.OrderByDescending(audit => audit.At).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 自动轮换（定时任务调用）：开了自动轮换的分组，当前证书满轮换周期或剩余有效期不足
    /// <see cref="CertificatePurposes.ExpiryMargin"/>、又还没有待生效的接替证书时，生成一张按提前量生效的新证书。
    /// 多实例同时跑时用咨询锁保证只有一个在生成。
    /// </summary>
    public async Task<RotationResult> RotateDueAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.TryAcquireTransactionLockAsync(RotationLockKey, cancellationToken))
            return new RotationResult(0, []);

        var now = time.GetUtcNow();
        var rows = await db.Certificates.AsNoTracking()
            .Where(certificate => certificate.Purpose != CertificatePurposes.TlsCode && certificate.RevokedAt == null)
            .Select(ViewRow)
            .ToListAsync(cancellationToken);

        var rotated = 0;
        var unattended = new List<string>();
        foreach (var group in rows.GroupBy(row => (row.Consumer, row.Purpose, row.Slot)))
        {
            var purpose = CertificatePurposes.Find(group.Key.Purpose);
            if (purpose is null)
                continue;

            var states = Classify(group, now);
            var active = group.FirstOrDefault(row => states[row.Id].Stage == CertificateStage.Active);
            if (active is null || group.Any(row => states[row.Id].Stage == CertificateStage.Staged))
                continue;

            var due = now >= active.ActivateAt + purpose.RotateAfter || active.NotAfter - now <= CertificatePurposes.ExpiryMargin;
            if (!due)
                continue;
            if (!active.AutoRotate)
            {
                if (active.NotAfter - now <= CertificatePurposes.ExpiryMargin)
                    unattended.Add($"{group.Key.Consumer}/{group.Key.Purpose}（{active.NotAfter:yyyy-MM-dd} 到期，未开启自动轮换）");
                continue;
            }

            var activateAt = now + purpose.ActivationLead;
            using var material = CertificateFiles.Generate(purpose, group.Key.Consumer, activateAt, purpose.Validity);
            await AddAsync(
                new NewCertificate(group.Key.Consumer, purpose.Code, group.Key.Slot, null, activateAt, active.Exportable, AutoRotate: true),
                material,
                CertificateSource.Generated,
                null,
                SystemActor,
                cancellationToken);
            rotated++;
            LogRotated(group.Key.Consumer, purpose.Code, activateAt);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RotationResult(rotated, unattended);
    }

    internal void AddAudit(string actor, string action, Guid? certificateId, Guid? subscriptionId, string? detail) =>
        db.Audits.Add(new CertificateAudit
        {
            Id = Guid.CreateVersion7(),
            At = time.GetUtcNow(),
            Actor = actor,
            Action = action,
            CertificateId = certificateId,
            AcmeSubscriptionId = subscriptionId,
            Detail = detail is { Length: > 2000 } ? detail[..2000] : detail
        });

    /// <summary>
    /// 新证书的生效时间：指定了就用指定的（早于现在按现在算）；没指定时，分组里还没有当前证书就立即生效，
    /// 否则按用途的提前量排期，让各实例先加载、签名公钥先发布出去。
    /// </summary>
    public async Task<DateTimeOffset> ResolveActivateAtAsync(
        string consumer,
        CertificatePurpose purpose,
        string? slot,
        DateTimeOffset? requested,
        CancellationToken cancellationToken)
    {
        if (!purpose.AllowsConsumer(consumer))
            throw new CertificateRuleException($"{purpose.DisplayName}只能给 {string.Join(" / ", purpose.Consumers)} 使用。");

        var now = time.GetUtcNow();
        if (requested is { } at)
            return at < now ? now : at;

        var normalizedSlot = NormalizeSlot(purpose, slot);
        var group = await db.Certificates.AsNoTracking()
            .Where(certificate => certificate.Consumer == consumer && certificate.Purpose == purpose.Code && certificate.Slot == normalizedSlot)
            .Select(ViewRow)
            .ToListAsync(cancellationToken);
        var hasActive = Classify(group, now).Values.Any(state => state.Stage == CertificateStage.Active);
        return hasActive ? now + purpose.ActivationLead : now;
    }

    private async Task<ManagedCertificate> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Certificates.SingleOrDefaultAsync(certificate => certificate.Id == id, cancellationToken)
        ?? throw new CertificateNotFoundException();

    private Task<List<Row>> GroupRowsAsync(ManagedCertificate certificate, CancellationToken cancellationToken) =>
        db.Certificates.AsNoTracking()
            .Where(row => row.Consumer == certificate.Consumer && row.Purpose == certificate.Purpose && row.Slot == certificate.Slot)
            .Select(ViewRow)
            .ToListAsync(cancellationToken);

    private static IReadOnlyDictionary<Guid, CertificateState> Classify(IEnumerable<Row> group, DateTimeOffset now)
    {
        var rows = group.ToList();
        var retention = CertificatePurposes.Find(rows.FirstOrDefault()?.Purpose)?.Retention ?? TimeSpan.Zero;
        return CertificateTimeline.Classify(
            rows.Select(row => new CertificateTimelineEntry(row.Id, row.ActivateAt, row.NotAfter, row.RevokedAt)),
            retention,
            now);
    }

    private static string NormalizeSlot(CertificatePurpose purpose, string? slot)
    {
        if (purpose.Code != CertificatePurposes.TlsCode)
            return CertificatePurposes.DefaultSlot;

        var value = slot?.Trim().ToLowerInvariant() ?? string.Empty;
        return SlotPattern().IsMatch(value)
            ? value
            : throw new CertificateRuleException("TLS 证书要指定站点名：小写字母、数字、点和短横线，最长 64 个字符（也是 nginx 证书目录名）。");
    }

    internal static string ValidateSlot(string? slot) => NormalizeSlot(CertificatePurposes.Tls, slot);

    private static CertificateView ToView(Row row, CertificateState state) => new(
        row.Id, row.Consumer, row.Purpose, row.Slot, row.Name, row.Source, state.Stage, state.RetireAt,
        row.Subject, row.Issuer, row.Thumbprint, row.KeyAlgorithm, row.DnsNames, row.NotBefore, row.NotAfter,
        row.ActivateAt, row.RevokedAt, row.RevokedReason, row.Exportable, row.AutoRotate, row.AcmeSubscriptionId,
        row.CreatedAt, row.CreatedBy);

    // 列表与轮换不需要私钥和证书正文，投影掉这两个大字段。
    private static readonly System.Linq.Expressions.Expression<Func<ManagedCertificate, Row>> ViewRow = certificate => new Row(
        certificate.Id, certificate.Consumer, certificate.Purpose, certificate.Slot, certificate.Name, certificate.Source,
        certificate.Subject, certificate.Issuer, certificate.Thumbprint, certificate.KeyAlgorithm, certificate.DnsNames,
        certificate.NotBefore, certificate.NotAfter, certificate.ActivateAt, certificate.RevokedAt, certificate.RevokedReason,
        certificate.Exportable, certificate.AutoRotate, certificate.AcmeSubscriptionId, certificate.CreatedAt, certificate.CreatedBy);

    private sealed record Row(
        Guid Id, string Consumer, string Purpose, string Slot, string Name, CertificateSource Source,
        string Subject, string Issuer, string Thumbprint, string KeyAlgorithm, string[] DnsNames,
        DateTimeOffset NotBefore, DateTimeOffset NotAfter, DateTimeOffset ActivateAt, DateTimeOffset? RevokedAt,
        string? RevokedReason, bool Exportable, bool AutoRotate, Guid? AcmeSubscriptionId, DateTimeOffset CreatedAt, string CreatedBy);

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex SlotPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Private key of certificate {CertificateId} was exported by {Actor}.")]
    private partial void LogPrivateKeyExported(Guid certificateId, string actor);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated a successor certificate for {Consumer}/{Purpose}, active from {ActivateAt}.")]
    private partial void LogRotated(string consumer, string purpose, DateTimeOffset activateAt);
}

/// <param name="Unattended">快到期却没开自动轮换的分组，定时任务据此告警。</param>
public sealed record RotationResult(int Rotated, IReadOnlyList<string> Unattended);

public sealed class CertificateConflictException(string message) : Exception(message);

public sealed class CertificateNotFoundException() : Exception("The certificate does not exist.");
