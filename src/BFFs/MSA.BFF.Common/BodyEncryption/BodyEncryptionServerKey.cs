using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;
using MSA.Certificates;

namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// 一把服务端 ECDH 密钥。只持有数值参数（不是 ECDiffieHellman 实例本身）：.NET 没有承诺 ECDiffieHellman
/// 并发调用 DeriveRawSecretAgreement 线程安全，不同平台后端也可能不一样，所以每次请求用这份参数现建一个，
/// 用完就扔——不生成新私钥，只是把已有的数字装进新对象，成本很低。
/// </summary>
public sealed class BodyEncryptionServerKey
{
    private readonly ECParameters _parameters;

    public BodyEncryptionServerKey(ECDiffieHellman key)
    {
        _parameters = key.ExportParameters(includePrivateParameters: true);
        PublicKeyRaw = EcPointCodec.ExportRawPublicKey(key);
        Kid = Base64Url.EncodeToString(SHA256.HashData(PublicKeyRaw).AsSpan(0, 12));
    }

    /// <summary>未压缩点格式（0x04||X||Y，65 字节），给 /bff/security/public-key 直接返回。</summary>
    public byte[] PublicKeyRaw { get; }

    /// <summary>密钥标识：公钥哈希的前 12 字节。浏览器带着它发请求，服务端据此在轮换期间选对密钥。</summary>
    public string Kid { get; }

    public ECDiffieHellman CreateDiffieHellman() => ECDiffieHellman.Create(_parameters);
}

/// <summary>
/// 请求体加密的服务端密钥环：当前密钥给新页面（公钥接口返回它），轮换后旧密钥在保留期内仍能解密
/// 还开着的页面发来的请求。密钥来自证书库（body-encryption），证书库变化时自动重建，不用重启；
/// 证书库里还没有时用环境变量证书（开发环境随机生成一把，进程内不变）。
/// </summary>
public sealed class BodyEncryptionKeyRing : IDisposable
{
    private readonly CertificateStore _store;
    private readonly Lazy<BodyEncryptionServerKey> _fallback;
    private readonly IDisposable? _subscription;
    private volatile Snapshot _snapshot;

    public BodyEncryptionKeyRing(CertificateStore store, IWebHostEnvironment environment)
    {
        _store = store;
        _fallback = new Lazy<BodyEncryptionServerKey>(() =>
        {
            using var key = BodyEncryptionCertificateEnvironment.Load(environment);
            return new BodyEncryptionServerKey(key);
        });
        _snapshot = Build();
        if (store.IsEnabled)
            _subscription = ChangeToken.OnChange(store.GetChangeToken, () => _snapshot = Build());
    }

    public BodyEncryptionServerKey Current => _snapshot.Current;

    public bool TryGet(string kid, [NotNullWhen(true)] out BodyEncryptionServerKey? key) =>
        _snapshot.Keys.TryGetValue(kid, out key);

    public void Dispose() => _subscription?.Dispose();

    private Snapshot Build()
    {
        var stored = _store.GetUsable(CertificatePurposes.BodyEncryptionCode);
        if (stored.Count == 0)
            return new Snapshot(_fallback.Value, new Dictionary<string, BodyEncryptionServerKey> { [_fallback.Value.Kid] = _fallback.Value });

        var keys = stored.Select(certificate =>
        {
            using var key = certificate.Certificate.GetECDiffieHellmanPrivateKey()
                ?? throw new InvalidOperationException($"Body encryption certificate {certificate.Id} has no ECDH private key.");
            return new BodyEncryptionServerKey(key);
        }).ToList();
        return new Snapshot(keys[0], keys.ToDictionary(key => key.Kid, StringComparer.Ordinal));
    }

    private sealed record Snapshot(BodyEncryptionServerKey Current, IReadOnlyDictionary<string, BodyEncryptionServerKey> Keys);
}
