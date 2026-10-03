using System.Security.Cryptography;

namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// P-256 公钥的未压缩点编码（0x04 || X(32) || Y(32)，65 字节）——跟浏览器 WebCrypto 的
/// exportKey("raw", ...) / importKey("raw", ...) 是同一种格式，不用 SPKI/DER 包装，省字节也省解析。
/// </summary>
internal static class EcPointCodec
{
    public const int RawPointLength = 65;

    public static byte[] ExportRawPublicKey(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var raw = new byte[RawPointLength];
        raw[0] = 0x04;
        parameters.Q.X!.CopyTo(raw, 1);
        parameters.Q.Y!.CopyTo(raw, 33);
        return raw;
    }

    public static ECDiffieHellman ImportRawPublicKey(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != RawPointLength || raw[0] != 0x04)
            throw new CryptographicException("Invalid uncompressed P-256 point.");

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = raw[1..33].ToArray(), Y = raw[33..65].ToArray() }
        };
        return ECDiffieHellman.Create(parameters);
    }
}
