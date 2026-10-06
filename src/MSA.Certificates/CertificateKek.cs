using System.Security.Cryptography;
using System.Text;

namespace MSA.Certificates;

/// <summary>
/// 使用方的密钥加密密钥（KEK）：32 字节 AES-256-GCM 密钥，base64 放在环境变量 MSA_CERT_KEK_&lt;使用方&gt;
/// （例如 MSA_CERT_KEK_BFF_WEB），跟 MSA_CLIENT_SECRET_* 一样只注入给对应的容器，证书服务拿全部。
/// 库里的私钥、DataProtection 密钥环都用它加密；附加数据把密文绑定到具体的行与用途，密文挪到别的行解不开。
/// </summary>
public sealed class CertificateKek
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public CertificateKek(string consumer, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
            throw new ArgumentException($"A certificate KEK must be exactly {KeySize} bytes.", nameof(key));

        Consumer = consumer;
        _key = key.ToArray();
        Id = Convert.ToHexStringLower(SHA256.HashData(_key).AsSpan(0, 8));
    }

    public string Consumer { get; }

    /// <summary>KEK 指纹，跟密文一起存：换了 KEK 能报出明确的错误，而不是笼统的"解密失败"。</summary>
    public string Id { get; }

    public static string VariableName(string consumer) =>
        "MSA_CERT_KEK_" + consumer.Replace('-', '_').ToUpperInvariant();

    public static bool IsConfigured(string consumer) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName(consumer)));

    public static CertificateKek FromEnvironment(string consumer)
    {
        var variable = VariableName(consumer);
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{variable} is required to read the certificate store. Generate one with `openssl rand -base64 32`.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{variable} must be base64.", exception);
        }

        return key.Length == KeySize
            ? new CertificateKek(consumer, key)
            : throw new InvalidOperationException($"{variable} must decode to exactly {KeySize} bytes.");
    }

    /// <summary>证书私钥的附加数据：一行一个。</summary>
    public static string CertificateContext(Guid certificateId, string consumer, string purpose) =>
        $"msa-cert:{consumer}:{purpose}:{certificateId:N}";

    /// <summary>证书服务自己的秘密（ACME 账户私钥等）的附加数据。</summary>
    public static string SecretContext(string kind, Guid id) => $"msa-secret:{kind}:{id:N}";

    /// <returns>nonce(12) || 密文 || tag(16)</returns>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string context)
    {
        var envelope = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = envelope.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            envelope.AsSpan(NonceSize, plaintext.Length),
            envelope.AsSpan(NonceSize + plaintext.Length),
            Encoding.UTF8.GetBytes(context));
        return envelope;
    }

    /// <exception cref="CryptographicException">密文被改过、附加数据不符或 KEK 不对。</exception>
    public byte[] Decrypt(ReadOnlySpan<byte> envelope, string context)
    {
        if (envelope.Length < NonceSize + TagSize)
            throw new CryptographicException("The encrypted payload is truncated.");

        var plaintext = new byte[envelope.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            envelope[..NonceSize],
            envelope.Slice(NonceSize, plaintext.Length),
            envelope[^TagSize..],
            plaintext,
            Encoding.UTF8.GetBytes(context));
        return plaintext;
    }
}
