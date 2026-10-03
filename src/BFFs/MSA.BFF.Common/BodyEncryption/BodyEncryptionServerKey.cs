using System.Security.Cryptography;

namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// 持有服务端 ECDH 密钥的数值参数（不是 ECDiffieHellman 实例本身）。.NET 的 ECDiffieHellman 对象
/// 并发调用 DeriveRawSecretAgreement 有没有保证线程安全没有文档承诺，不同平台（Windows CNG / Linux OpenSSL）
/// 后端实现也可能不一样，所以不把一个实例当单例到处复用——每次请求用这份参数现建一个，用完就扔，
/// 成本很低（不用生成新私钥，纯粹是把已有的数字装进一个新对象），换来的是不用担心并发正确性。
/// </summary>
public sealed class BodyEncryptionServerKey
{
    private readonly ECParameters _parameters;

    public BodyEncryptionServerKey(ECDiffieHellman key)
    {
        _parameters = key.ExportParameters(includePrivateParameters: true);
        PublicKeyRaw = EcPointCodec.ExportRawPublicKey(key);
    }

    /// <summary>未压缩点格式（0x04||X||Y，65 字节），给 /bff/security/public-key 直接返回。</summary>
    public byte[] PublicKeyRaw { get; }

    public ECDiffieHellman CreateDiffieHellman() => ECDiffieHellman.Create(_parameters);
}
