using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Infrastructure;

public sealed class ObjectStorageOptions
{
    internal const string SectionName = "ObjectStorage";

    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string PrivateBucket { get; init; } = string.Empty;
    /// <summary>签名地域。RustFS/MinIO 随便填；腾讯云 COS 填桶所在地域，如 ap-guangzhou。</summary>
    public string Region { get; init; } = "us-east-1";
    /// <summary>COS 2024 年后新建的桶只认虚拟主机风格 {bucket}.{host}，要设为 false。</summary>
    public bool ForcePathStyle { get; init; } = true;
    /// <summary>桶开了 Referer 防盗链时服务端的 HEAD/DELETE 也会被拒，这里填白名单里的一个地址。</summary>
    public string Referer { get; init; } = string.Empty;

    internal static bool IsValid(ObjectStorageOptions options) =>
        Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
        && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(options.PrivateBucket)
        && !string.IsNullOrWhiteSpace(options.AccessKey)
        && !string.IsNullOrWhiteSpace(options.SecretKey);
}

public sealed record EvidenceUploadTicket(
    string ObjectKey,
    string UploadUrl,
    DateTimeOffset ExpiresOnUtc,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// 售后凭证放在私有桶：浏览器凭短期签名地址直传与读取，服务端只保存对象键。
/// 对象键里嵌入申请人摘要，提交售后时据此拒绝引用他人上传的图片。
/// </summary>
public sealed partial class AfterSalesEvidenceStorage : IDisposable
{
    public const long MaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<string, (string Stored, string[] Sources)> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ("jpg", [".jpg", ".jpeg"]),
        ["image/png"] = ("png", [".png"]),
        ["image/webp"] = ("webp", [".webp"])
    };

    private readonly ObjectStorageOptions options;
    private readonly TimeProvider clock;
    private readonly AmazonS3Client client;
    private readonly Protocol protocol;

    public AfterSalesEvidenceStorage(IOptions<ObjectStorageOptions> options, TimeProvider clock)
    {
        this.options = options.Value;
        this.clock = clock;
        var endpoint = new Uri(this.options.Endpoint, UriKind.Absolute);
        protocol = endpoint.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP;
        client = new AmazonS3Client(
            new BasicAWSCredentials(this.options.AccessKey, this.options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint.AbsoluteUri.TrimEnd('/'),
                AuthenticationRegion = this.options.Region,
                ForcePathStyle = this.options.ForcePathStyle
            });
        if (!string.IsNullOrEmpty(this.options.Referer))
        {
            var referer = this.options.Referer;
            client.BeforeRequestEvent += (_, args) =>
            {
                if (args is WebServiceRequestEventArgs request)
                    request.Headers["Referer"] = referer;
            };
        }
    }

    public EvidenceUploadTicket CreateUploadUrl(string userId, string fileName, string contentType, long contentLength)
    {
        if (!ImageTypes.TryGetValue(contentType?.Trim() ?? string.Empty, out var type))
            throw new AfterSalesRuleException("只支持 JPG、PNG 或 WebP 图片。");
        if (!type.Sources.Contains(Path.GetExtension(fileName ?? string.Empty), StringComparer.OrdinalIgnoreCase))
            throw new AfterSalesRuleException("图片扩展名与格式不符。");
        if (contentLength is <= 0 or > MaxBytes)
            throw new AfterSalesRuleException("单张图片不能超过 10 MB。");

        var normalizedType = contentType!.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();
        var expiresOnUtc = now.Add(UploadUrlLifetime);
        // 引号包住分隔符：裸 "/" 是区域性日期分隔符，会破坏对象键格式。
        var objectKey = $"after-sales/{OwnerSegment(userId)}/{now.ToString("yyyy'/'MM", CultureInfo.InvariantCulture)}/{Guid.NewGuid():N}.{type.Stored}";
        var uploadUrl = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.PrivateBucket,
            Key = objectKey,
            ContentType = normalizedType,
            Verb = HttpVerb.PUT,
            Protocol = protocol,
            Expires = expiresOnUtc.UtcDateTime
        });
        return new EvidenceUploadTicket(objectKey, uploadUrl, expiresOnUtc, new Dictionary<string, string> { ["Content-Type"] = normalizedType });
    }

    public static bool IsOwnedBy(string objectKey, string userId) =>
        KeyPattern().Match(objectKey) is { Success: true } match && match.Groups["owner"].Value == OwnerSegment(userId);

    /// <summary>
    /// 签名地址无法限制大小，提交前回查对象：未上传或超限的图片不允许挂到售后单上，超限对象顺手删除。
    /// </summary>
    public async Task EnsureUploadedAsync(string objectKey, CancellationToken cancellationToken)
    {
        GetObjectMetadataResponse metadata;
        try
        {
            metadata = await client.GetObjectMetadataAsync(options.PrivateBucket, objectKey, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AfterSalesRuleException("有凭证图片未上传成功或已过期，请重新上传。");
        }

        if (metadata.ContentLength is <= 0 or > MaxBytes)
        {
            await client.DeleteObjectAsync(options.PrivateBucket, objectKey, cancellationToken);
            throw new AfterSalesRuleException("凭证图片超过 10 MB，已被移除，请压缩后重新上传。");
        }
    }

    public string CreateReadUrl(string objectKey) =>
        client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.PrivateBucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = protocol,
            Expires = clock.GetUtcNow().Add(ReadUrlLifetime).UtcDateTime
        });

    private static string OwnerSegment(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..32];

    [GeneratedRegex(@"^after-sales/(?<owner>[0-9a-f]{32})/\d{4}/\d{2}/[0-9a-f]{32}\.(jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public void Dispose() => client.Dispose();
}
