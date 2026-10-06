using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace MSA.BFF.Common.BodyEncryption;

/// <summary>
/// 浏览器 ⇄ BFF 的请求/响应体加密：ECDH(P-256) 协商 + HKDF-SHA256 派生 + AES-256-GCM。
/// 客户端每次请求生成一个临时密钥对，公钥放在 X-Msa-Ekey 头里（base64，65 字节未压缩点）；
/// 跟服务端静态密钥一次性协商出共享密钥，按方向分别派生两个 AES 密钥（c2s/s2c）用完就扔——
/// 无状态，服务端不用存会话密钥，也不用管密钥过期。
///
/// 密钥放在头里而不是塞进请求体：GET 请求没有 body 可解密，但响应仍然需要加密，
/// 两者要能独立工作。
///
/// 请求体（如果有）：Content-Type = application/vnd.msa.enc，body = nonce(12) || ciphertext+tag。
/// 响应体：同样格式，用 s2c 密钥、新随机 nonce。没有 X-Msa-Ekey 头的请求完全不受影响（透传），
/// 方便现有调用方逐步升级，不是一刀切强制。
///
/// AEAD（GCM）天然要求拿到完整消息才能校验 tag，所以响应必须整段缓冲再加密——SSE、文件上传/下载
/// 这类流式或大体量端点要用 [SkipBodyEncryption] 跳过，否则等于把整个响应塞进内存。
///
/// 服务端密钥会轮换：浏览器在 X-Msa-Kid 头里带上它拿到的公钥标识，服务端用对应的密钥（当前或保留期内的旧密钥）。
/// 标识认不出来（旧密钥已退役）时返回 400 并带 X-Msa-Kid-Unknown 头，前端据此重拉公钥再重试一次。
/// 不带 X-Msa-Kid 的旧客户端按当前密钥处理。
/// </summary>
public sealed class BodyEncryptionMiddleware(BodyEncryptionKeyRing keyRing, ILogger<BodyEncryptionMiddleware> logger) : IMiddleware
{
    public const string ContentType = "application/vnd.msa.enc";
    public const string EphemeralKeyHeader = "X-Msa-Ekey";
    public const string KeyIdHeader = "X-Msa-Kid";
    public const string UnknownKeyHeader = "X-Msa-Kid-Unknown";

    private static readonly byte[] RequestInfo = "msa-body-c2s"u8.ToArray();
    private static readonly byte[] ResponseInfo = "msa-body-s2c"u8.ToArray();
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<SkipBodyEncryptionAttribute>() is not null)
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(EphemeralKeyHeader, out var headerValue))
        {
            await next(context);
            return;
        }

        var kid = context.Request.Headers[KeyIdHeader].ToString();
        BodyEncryptionServerKey? serverKey;
        if (kid.Length == 0)
        {
            serverKey = keyRing.Current;
        }
        else if (!keyRing.TryGet(kid, out serverKey))
        {
            // 不加密这个响应：浏览器手里的公钥已经对不上了，加了它也解不开。
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers[UnknownKeyHeader] = "1";
            return;
        }

        byte[] c2sKey, s2cKey;
        try
        {
            (c2sKey, s2cKey) = DeriveKeys(serverKey, headerValue.ToString());
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            logger.LogWarning(ex, "Rejected malformed {Header} header.", EphemeralKeyHeader);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (context.Request.ContentLength is > 0)
        {
            if (!string.Equals(context.Request.ContentType, ContentType, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!await TryDecryptRequestBodyAsync(context, c2sKey))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }

        var originalResponseBody = context.Response.Body;
        using var buffered = new MemoryStream();
        context.Response.Body = buffered;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalResponseBody;
        }

        await WriteEncryptedResponseAsync(context, buffered, s2cKey, originalResponseBody);
    }

    private static (byte[] C2S, byte[] S2C) DeriveKeys(BodyEncryptionServerKey serverKey, string headerValue)
    {
        var clientPublicKeyRaw = Convert.FromBase64String(headerValue);

        using var serverDh = serverKey.CreateDiffieHellman();
        using var clientPublicKey = EcPointCodec.ImportRawPublicKey(clientPublicKeyRaw);

        var secret = serverDh.DeriveRawSecretAgreement(clientPublicKey.PublicKey);
        try
        {
            var c2s = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: RequestInfo);
            var s2c = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: ResponseInfo);
            return (c2s, s2c);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private async Task<bool> TryDecryptRequestBodyAsync(HttpContext context, byte[] key)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var envelope = ms.ToArray();
        if (envelope.Length < NonceLength + TagLength)
        {
            logger.LogWarning("Encrypted request body is shorter than the minimum envelope size.");
            return false;
        }

        var nonce = envelope.AsSpan(0, NonceLength);
        var cipherAndTag = envelope.AsSpan(NonceLength);
        var tag = cipherAndTag[^TagLength..];
        var ciphertext = cipherAndTag[..^TagLength];
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var gcm = new AesGcm(key, TagLength);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Failed to decrypt request body: bad tag, wrong key, or tampered ciphertext.");
            return false;
        }

        context.Request.Body = new MemoryStream(plaintext);
        context.Request.ContentLength = plaintext.Length;
        context.Request.ContentType = "application/json; charset=utf-8";
        return true;
    }

    private static async Task WriteEncryptedResponseAsync(HttpContext context, MemoryStream buffered, byte[] key, Stream destination)
    {
        if (buffered.Length == 0)
        {
            // 没有响应体（204、HEAD 等）：没什么可加密的，状态码/响应头照常发出去。
            return;
        }

        var plaintext = buffered.ToArray();
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var gcm = new AesGcm(key, TagLength))
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var envelope = new byte[NonceLength + ciphertext.Length + TagLength];
        nonce.CopyTo(envelope, 0);
        ciphertext.CopyTo(envelope, NonceLength);
        tag.CopyTo(envelope, NonceLength + ciphertext.Length);

        context.Response.ContentType = ContentType;
        context.Response.ContentLength = envelope.Length;
        await destination.WriteAsync(envelope);
    }
}
