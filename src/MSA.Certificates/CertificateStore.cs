using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Primitives;

namespace MSA.Certificates;

/// <summary>一张已解密、带私钥的证书。</summary>
public sealed record StoredCertificate(
    Guid Id,
    string Purpose,
    string Slot,
    DateTimeOffset ActivateAt,
    DateTimeOffset NotAfter,
    X509Certificate2 Certificate);

/// <summary>
/// 本使用方在证书库里的证书（内存快照）。由 <see cref="CertificateStoreRefresher"/> 定期从 CertificateDb 刷新；
/// 证书变了、或时间跨过某张证书的激活 / 退役点时，<see cref="GetChangeToken"/> 触发，OpenIddict 选项、
/// 请求体加密密钥环据此重建，不用重启。
/// <para>
/// 某个用途"有当前证书"才算由证书库接管；否则调用方继续用环境变量里的证书（开发环境用开发证书），
/// 所以迁移时先把现有 pfx 导入并立即生效，再上新版本，签发中的令牌不受影响。
/// </para>
/// </summary>
public sealed class CertificateStore
{
    private readonly TimeProvider _time;
    private volatile IReadOnlyList<StoredCertificate> _certificates = [];
    private CancellationTokenSource _changes = new();

    internal CertificateStore(string consumer, bool isEnabled, TimeProvider time)
    {
        Consumer = consumer;
        IsEnabled = isEnabled;
        _time = time;
    }

    /// <summary>没有配置证书库时的空实现：所有用途都退回环境变量证书 / 开发证书。</summary>
    public static CertificateStore Disabled { get; } = new(string.Empty, isEnabled: false, TimeProvider.System);

    public string Consumer { get; }

    public bool IsEnabled { get; }

    /// <summary>
    /// 此刻可用的证书（当前、待生效、退役中），当前证书排第一个。没有当前证书时返回空，
    /// 表示这个用途还没交给证书库管理。
    /// </summary>
    public IReadOnlyList<StoredCertificate> GetUsable(string purpose, string slot = CertificatePurposes.DefaultSlot)
    {
        var group = _certificates.Where(certificate => certificate.Purpose == purpose && certificate.Slot == slot).ToList();
        if (group.Count == 0)
            return [];

        var states = CertificateTimeline.Classify(
            group.Select(certificate => new CertificateTimelineEntry(certificate.Id, certificate.ActivateAt, certificate.NotAfter, null)),
            CertificatePurposes.Get(purpose).Retention,
            _time.GetUtcNow());
        var active = group.Find(certificate => states[certificate.Id].Stage == CertificateStage.Active);
        if (active is null)
            return [];

        return [active, .. group
            .Where(certificate => certificate != active && states[certificate.Id].IsUsable)
            .OrderByDescending(certificate => certificate.ActivateAt)];
    }

    public StoredCertificate? GetActive(string purpose, string slot = CertificatePurposes.DefaultSlot) =>
        GetUsable(purpose, slot).FirstOrDefault();

    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changes).Token);

    internal void Publish(IReadOnlyList<StoredCertificate> certificates)
    {
        // 旧快照里的 X509Certificate2 不主动 Dispose：可能还有进行中的请求拿着它签名，交给 GC。
        _certificates = certificates;
        NotifyChanged();
    }

    internal void NotifyChanged() => Interlocked.Exchange(ref _changes, new CancellationTokenSource()).Cancel();

    /// <summary>每个分组此刻的可用证书及顺序。时间跨过激活 / 退役点时它会变，刷新器据此触发重建。</summary>
    internal string DescribeState() => string.Join('|', _certificates
        .Select(certificate => (certificate.Purpose, certificate.Slot))
        .Distinct()
        .Order()
        .Select(group => $"{group.Purpose}/{group.Slot}:{string.Join(',', GetUsable(group.Purpose, group.Slot).Select(certificate => certificate.Id))}"));
}
