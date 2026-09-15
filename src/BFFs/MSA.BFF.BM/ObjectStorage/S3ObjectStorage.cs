using System.Diagnostics.CodeAnalysis;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace MSA.BFF.BM.ObjectStorage;

internal sealed class ObjectStorageOptions
{
    internal const string SectionName = "ObjectStorage";

    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string PublicBucket { get; init; } = string.Empty;
    public string PrivateBucket { get; init; } = string.Empty;

    internal static bool IsValid(ObjectStorageOptions options) =>
        Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) &&
        (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        string.IsNullOrEmpty(endpoint.Query) &&
        string.IsNullOrEmpty(endpoint.Fragment) &&
        endpoint.AbsolutePath == "/" &&
        IsBucketName(options.PublicBucket) &&
        IsBucketName(options.PrivateBucket) &&
        !string.Equals(options.PublicBucket, options.PrivateBucket, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(options.AccessKey) &&
        !string.IsNullOrWhiteSpace(options.SecretKey);

    private static bool IsBucketName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 3 and <= 63 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.');
}

internal enum ObjectStorageArea
{
    CatalogMedia,
    RefundEvidence
}

internal sealed class S3ObjectStorage : IDisposable
{
    private static readonly IReadOnlyDictionary<string, UploadPolicy> UploadPolicies =
        new Dictionary<string, UploadPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = new("jpg", [".jpg", ".jpeg"], 20 * 1024 * 1024, true),
            ["image/png"] = new("png", [".png"], 20 * 1024 * 1024, true),
            ["image/webp"] = new("webp", [".webp"], 20 * 1024 * 1024, true),
            ["image/gif"] = new("gif", [".gif"], 20 * 1024 * 1024, true),
            ["image/avif"] = new("avif", [".avif"], 20 * 1024 * 1024, true),
            ["video/mp4"] = new("mp4", [".mp4"], 512L * 1024 * 1024, false),
            ["video/webm"] = new("webm", [".webm"], 512L * 1024 * 1024, false),
            ["video/quicktime"] = new("mov", [".mov"], 512L * 1024 * 1024, false)
        };

    private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromMinutes(5);
    private readonly ObjectStorageOptions options;
    private readonly TimeProvider clock;
    private readonly AmazonS3Client client;
    private readonly Uri endpoint;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options, TimeProvider clock)
    {
        this.options = options.Value;
        this.clock = clock;
        endpoint = new Uri(this.options.Endpoint, UriKind.Absolute);
        client = new AmazonS3Client(
            new BasicAWSCredentials(this.options.AccessKey, this.options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint.AbsoluteUri.TrimEnd('/'),
                AuthenticationRegion = "us-east-1",
                ForcePathStyle = true
            });
    }

    internal bool TryCreateUploadUrl(
        ObjectStorageArea area,
        CreateObjectUploadUrlRequest request,
        [NotNullWhen(true)] out ObjectUploadUrlResponse? response,
        out Dictionary<string, string[]> errors)
    {
        errors = ValidateUpload(area, request);
        if (errors.Count > 0)
        {
            response = null;
            return false;
        }

        var contentType = request.ContentType.Trim().ToLowerInvariant();
        var policy = UploadPolicies[contentType];
        var isPublic = area == ObjectStorageArea.CatalogMedia;
        var bucket = isPublic ? options.PublicBucket : options.PrivateBucket;
        var prefix = isPublic ? "catalog-media" : "refund-evidence";
        var now = clock.GetUtcNow();
        var expiresOnUtc = now.Add(UploadUrlLifetime);
        var objectKey = $"{prefix}/{now:yyyy/MM}/{Guid.NewGuid():N}.{policy.StoredExtension}";
        var uploadUrl = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            ContentType = contentType,
            Verb = HttpVerb.PUT,
            Protocol = endpoint.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP,
            Expires = expiresOnUtc.UtcDateTime
        });

        response = new ObjectUploadUrlResponse(
            objectKey,
            $"s3://{bucket}/{objectKey}",
            isPublic ? BuildObjectUrl(bucket, objectKey) : null,
            uploadUrl,
            expiresOnUtc,
            new Dictionary<string, string> { ["Content-Type"] = contentType });
        return true;
    }

    internal bool TryCreateRefundReadUrl(
        string? objectKey,
        [NotNullWhen(true)] out PrivateObjectReadUrlResponse? response)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.Length > 1024 ||
            !objectKey.StartsWith("refund-evidence/", StringComparison.Ordinal) ||
            objectKey.Contains("..", StringComparison.Ordinal) ||
            objectKey.Any(char.IsControl))
        {
            response = null;
            return false;
        }

        var expiresOnUtc = clock.GetUtcNow().Add(ReadUrlLifetime);
        var accessUrl = client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.PrivateBucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = endpoint.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP,
            Expires = expiresOnUtc.UtcDateTime
        });
        response = new PrivateObjectReadUrlResponse(accessUrl, expiresOnUtc);
        return true;
    }

    private static Dictionary<string, string[]> ValidateUpload(
        ObjectStorageArea area,
        CreateObjectUploadUrlRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FileName) ||
            request.FileName.Length > 255 ||
            request.FileName != Path.GetFileName(request.FileName) ||
            request.FileName.Any(char.IsControl))
        {
            errors[nameof(request.FileName)] = ["FileName must be a plain file name with at most 255 characters."];
        }

        var contentType = request.ContentType?.Trim() ?? string.Empty;
        if (!UploadPolicies.TryGetValue(contentType, out var policy) ||
            area == ObjectStorageArea.RefundEvidence && !policy.IsImage)
        {
            errors[nameof(request.ContentType)] = [area == ObjectStorageArea.CatalogMedia
                ? "Only supported image and video content types can be uploaded."
                : "Only supported image content types can be uploaded as refund evidence."];
            return errors;
        }

        if (!policy.SourceExtensions.Contains(Path.GetExtension(request.FileName), StringComparer.OrdinalIgnoreCase))
            errors[nameof(request.FileName)] = ["The file extension does not match ContentType."];
        if (request.ContentLength is <= 0 || request.ContentLength > policy.MaxBytes)
            errors[nameof(request.ContentLength)] = [$"ContentLength must be between 1 and {policy.MaxBytes} bytes."];
        return errors;
    }

    private string BuildObjectUrl(string bucket, string objectKey) =>
        $"{endpoint.AbsoluteUri.TrimEnd('/')}/{Uri.EscapeDataString(bucket)}/{string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString))}";

    public void Dispose() => client.Dispose();

    private sealed record UploadPolicy(
        string StoredExtension,
        string[] SourceExtensions,
        long MaxBytes,
        bool IsImage);
}

/// <summary>Requests a short-lived direct-upload URL for an object.</summary>
public sealed record CreateObjectUploadUrlRequest(
    string FileName,
    string ContentType,
    long ContentLength);

/// <summary>Contains the storage identity and signed URL used for a direct upload.</summary>
public sealed record ObjectUploadUrlResponse(
    string ObjectKey,
    string StorageUri,
    string? PublicUrl,
    string UploadUrl,
    DateTimeOffset ExpiresOnUtc,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>Requests temporary read access to a private refund-evidence object.</summary>
public sealed record CreatePrivateObjectReadUrlRequest(string ObjectKey);

/// <summary>Contains a short-lived URL for reading a private object.</summary>
public sealed record PrivateObjectReadUrlResponse(string AccessUrl, DateTimeOffset ExpiresOnUtc);
