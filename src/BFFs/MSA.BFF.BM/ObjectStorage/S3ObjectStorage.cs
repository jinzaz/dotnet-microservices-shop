using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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

internal enum ObjectVerificationOutcome
{
    Verified,
    InvalidKey,
    NotFound,
    Mismatch
}

internal sealed partial class S3ObjectStorage : IDisposable
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

    /// <summary>Upload rules exposed to the BM UI so it can reject bad files before requesting a signed URL.</summary>
    internal static ObjectUploadPolicyResponse GetUploadPolicy(ObjectStorageArea area) =>
        new(UploadPolicies
            .Where(item => area == ObjectStorageArea.CatalogMedia || item.Value.IsImage)
            .Select(item => new ObjectContentTypePolicy(
                item.Key,
                item.Value.SourceExtensions,
                item.Value.MaxBytes,
                item.Value.IsImage ? "Image" : "Video"))
            .ToArray(),
            (int)UploadUrlLifetime.TotalSeconds);

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
        var bucket = BucketFor(area);
        var now = clock.GetUtcNow();
        var expiresOnUtc = now.Add(UploadUrlLifetime);
        // Quoted separator: an unquoted "/" is the culture date separator and would break the managed-key pattern.
        var objectKey = $"{PrefixFor(area)}/{now.ToString("yyyy'/'MM", CultureInfo.InvariantCulture)}/{Guid.NewGuid():N}.{policy.StoredExtension}";
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

    /// <summary>
    /// Confirms a browser upload landed with the size and type that were signed. A mismatched object is removed so
    /// that a truncated or substituted file can never be referenced by catalog data.
    /// </summary>
    internal async Task<(ObjectVerificationOutcome Outcome, VerifiedObjectResponse? Object)> VerifyUploadAsync(
        ObjectStorageArea area,
        VerifyObjectUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsManagedKey(area, request.ObjectKey))
            return (ObjectVerificationOutcome.InvalidKey, null);

        var bucket = BucketFor(area);
        GetObjectMetadataResponse metadata;
        try
        {
            metadata = await client.GetObjectMetadataAsync(bucket, request.ObjectKey, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return (ObjectVerificationOutcome.NotFound, null);
        }

        var expectedType = request.ContentType?.Trim() ?? string.Empty;
        if (metadata.ContentLength != request.ContentLength ||
            !string.Equals(metadata.Headers.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
        {
            await client.DeleteObjectAsync(bucket, request.ObjectKey, cancellationToken);
            return (ObjectVerificationOutcome.Mismatch, null);
        }

        return (ObjectVerificationOutcome.Verified, new VerifiedObjectResponse(
            request.ObjectKey,
            area == ObjectStorageArea.CatalogMedia ? BuildObjectUrl(bucket, request.ObjectKey) : null,
            metadata.Headers.ContentType,
            metadata.ContentLength,
            metadata.LastModified.HasValue ? new DateTimeOffset(metadata.LastModified.Value.ToUniversalTime()) : null));
    }

    /// <summary>Deletes a managed public catalog object identified by its key or the public URL stored in Catalog.</summary>
    internal async Task<bool> TryDiscardCatalogMediaAsync(
        DiscardObjectRequest request,
        CancellationToken cancellationToken)
    {
        var objectKey = request.ObjectKey ?? TryGetCatalogMediaKey(request.PublicUrl);
        if (!IsManagedKey(ObjectStorageArea.CatalogMedia, objectKey))
            return false;

        // S3 DELETE is idempotent, so retries and already-removed objects are both successful outcomes.
        await client.DeleteObjectAsync(options.PublicBucket, objectKey, cancellationToken);
        return true;
    }

    internal bool TryCreateRefundReadUrl(
        string? objectKey,
        [NotNullWhen(true)] out PrivateObjectReadUrlResponse? response)
    {
        if (!IsManagedKey(ObjectStorageArea.RefundEvidence, objectKey))
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

    internal static bool IsManagedKey(ObjectStorageArea area, [NotNullWhen(true)] string? objectKey) =>
        objectKey is not null && ManagedKeyPattern().Match(objectKey) is { Success: true } match &&
        match.Groups["prefix"].Value == PrefixFor(area) &&
        UploadPolicies.Values.Any(policy => policy.StoredExtension == match.Groups["extension"].Value &&
            (area == ObjectStorageArea.CatalogMedia || policy.IsImage));

    private string? TryGetCatalogMediaKey(string? publicUrl)
    {
        var prefix = BuildObjectUrl(options.PublicBucket, string.Empty);
        return publicUrl is not null && publicUrl.StartsWith(prefix, StringComparison.Ordinal)
            ? Uri.UnescapeDataString(publicUrl[prefix.Length..])
            : null;
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

    private string BucketFor(ObjectStorageArea area) =>
        area == ObjectStorageArea.CatalogMedia ? options.PublicBucket : options.PrivateBucket;

    private static string PrefixFor(ObjectStorageArea area) =>
        area == ObjectStorageArea.CatalogMedia ? "catalog-media" : "refund-evidence";

    private string BuildObjectUrl(string bucket, string objectKey) =>
        $"{endpoint.AbsoluteUri.TrimEnd('/')}/{Uri.EscapeDataString(bucket)}/{string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString))}";

    // Only keys minted by TryCreateUploadUrl are accepted; this blocks traversal and deletion of foreign objects.
    [GeneratedRegex(@"^(?<prefix>catalog-media|refund-evidence)/\d{4}/\d{2}/[0-9a-f]{32}\.(?<extension>[a-z0-9]{3,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedKeyPattern();

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

/// <summary>Confirms that a signed upload completed with the declared size and content type.</summary>
public sealed record VerifyObjectUploadRequest(string ObjectKey, string ContentType, long ContentLength);

/// <summary>Stored object metadata after a successful upload verification.</summary>
public sealed record VerifiedObjectResponse(
    string ObjectKey,
    string? PublicUrl,
    string ContentType,
    long ContentLength,
    DateTimeOffset? LastModifiedOnUtc);

/// <summary>Identifies a managed catalog object by key or by the public URL persisted in Catalog.</summary>
public sealed record DiscardObjectRequest(string? ObjectKey, string? PublicUrl);

/// <summary>Accepted content type and its size limit.</summary>
public sealed record ObjectContentTypePolicy(string ContentType, IReadOnlyList<string> Extensions, long MaxBytes, string MediaType);

/// <summary>Client-side validation rules mirroring the server upload policy.</summary>
public sealed record ObjectUploadPolicyResponse(IReadOnlyList<ObjectContentTypePolicy> ContentTypes, int UploadUrlLifetimeSeconds);

/// <summary>Requests temporary read access to a private refund-evidence object.</summary>
public sealed record CreatePrivateObjectReadUrlRequest(string ObjectKey);

/// <summary>Contains a short-lived URL for reading a private object.</summary>
public sealed record PrivateObjectReadUrlResponse(string AccessUrl, DateTimeOffset ExpiresOnUtc);
