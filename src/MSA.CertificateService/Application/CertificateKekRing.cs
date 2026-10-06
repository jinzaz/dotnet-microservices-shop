using MSA.Certificates;

namespace MSA.CertificateService.Application;

/// <summary>
/// 证书服务持有全部使用方的 KEK：写入时用使用方自己的 KEK 加密，那个使用方才解得开。
/// 启动时就全部读取，缺了任何一个直接起不来——比运行到导入那一刻才报错好排查。
/// </summary>
public sealed class CertificateKekRing
{
    private readonly IReadOnlyDictionary<string, CertificateKek> _keks;

    public CertificateKekRing(IEnumerable<CertificateKek> keks) =>
        _keks = keks.ToDictionary(kek => kek.Consumer, StringComparer.Ordinal);

    public static CertificateKekRing FromEnvironment() =>
        new(CertificateConsumers.All.Select(CertificateKek.FromEnvironment));

    public CertificateKek For(string consumer) =>
        _keks.TryGetValue(consumer, out var kek)
            ? kek
            : throw new InvalidOperationException($"No KEK is configured for certificate consumer '{consumer}'.");
}
