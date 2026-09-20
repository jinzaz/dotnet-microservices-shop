using System.Security.Cryptography;
using System.Text;

namespace MSA.CatalogService.Domain;

public sealed class Coupon : RevisionedCatalogAggregateRoot<Guid>
{
    private readonly List<CouponQuotaBucket> _quotaBuckets = [];

    private Coupon()
    {
    }

    public Coupon(
        Guid id,
        Guid promotionId,
        string code,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string? assignedUserId,
        int? totalLimit,
        int perUserLimit,
        CouponQuotaStrategy quotaStrategy,
        int? bucketCount,
        string actor,
        DateTimeOffset createdOnUtc,
        Guid? campaignId = null)
    {
        CatalogDomainGuard.ValidWindow(
            validFromUtc,
            validUntilUtc,
            nameof(validFromUtc),
            nameof(validUntilUtc));

        Id = CatalogDomainGuard.Required(id, nameof(id));
        PromotionId = CatalogDomainGuard.Required(promotionId, nameof(promotionId));
        CampaignId = campaignId;
        var normalizedCode = NormalizeCode(code);
        CodeDigest = ComputeCodeDigest(normalizedCode);
        MaskedCode = MaskCode(normalizedCode);
        ValidFromUtc = CatalogDomainGuard.Utc(validFromUtc);
        ValidUntilUtc = CatalogDomainGuard.Utc(validUntilUtc);
        AssignedUserId = CatalogDomainGuard.OptionalOrNull(assignedUserId, nameof(assignedUserId), 200);
        TotalLimit = ValidateTotalLimit(totalLimit);
        PerUserLimit = ValidatePerUserLimit(perUserLimit, TotalLimit);
        QuotaStrategy = quotaStrategy;
        _quotaBuckets.AddRange(BuildQuotaBuckets(Id, TotalLimit, quotaStrategy, bucketCount));
        Status = CouponStatus.Draft;
        InitializeAudit(actor, createdOnUtc);
        InitializeRevision();
    }

    public Guid PromotionId { get; private set; }

    /// <summary>领券活动标识；为空表示后台直接创建或批量发放的券。</summary>
    public Guid? CampaignId { get; private set; }

    /// <summary>标准化券码的 SHA-256 摘要；可核销券码明文永不落库。</summary>
    public string CodeDigest { get; private set; } = string.Empty;

    public string MaskedCode { get; private set; } = string.Empty;
    public CouponStatus Status { get; private set; }
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset ValidUntilUtc { get; private set; }
    public string? AssignedUserId { get; private set; }
    public int? TotalLimit { get; private set; }
    public int PerUserLimit { get; private set; }
    public CouponQuotaStrategy QuotaStrategy { get; private set; }
    public IReadOnlyCollection<CouponQuotaBucket> QuotaBuckets => _quotaBuckets;

    public static string NormalizeCode(string code) =>
        NormalizeAndValidateCode(code);

    // Omits 0/O and 1/I so printed or read-aloud codes are not mistyped.
    private const string GeneratedCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Creates a random, human-typeable code; callers must still check digest uniqueness.</summary>
    public static string GenerateCode(string? prefix = null, int randomLength = 10)
    {
        if (randomLength is < 6 or > 32)
            throw new ArgumentOutOfRangeException(nameof(randomLength), "Generated code length must be between 6 and 32.");
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim().ToUpperInvariant();
        if (normalizedPrefix.Length > 20 || normalizedPrefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Code prefix must be at most 20 letters, digits or hyphens.", nameof(prefix));
        var random = new string(RandomNumberGenerator.GetItems<char>(GeneratedCodeAlphabet, randomLength));
        return NormalizeCode(normalizedPrefix + random);
    }

    public static string ComputeCodeDigest(string code)
    {
        var normalized = NormalizeCode(code);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string MaskCode(string code)
    {
        var normalized = NormalizeCode(code);
        var visibleCharacters = Math.Min(4, normalized.Length);
        return $"****{normalized[^visibleCharacters..]}";
    }

    public bool IsAvailableFor(string userId, DateTimeOffset instantUtc)
    {
        var normalizedUser = CatalogDomainGuard.Required(userId, nameof(userId), 200);
        var utc = CatalogDomainGuard.Utc(instantUtc);
        return Status == CouponStatus.Active
            && ValidFromUtc <= utc
            && utc < ValidUntilUtc
            && (AssignedUserId is null
                || string.Equals(AssignedUserId, normalizedUser, StringComparison.Ordinal));
    }

    public void ChangeValidityWindow(
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        CatalogDomainGuard.ValidWindow(
            validFromUtc,
            validUntilUtc,
            nameof(validFromUtc),
            nameof(validUntilUtc));
        var normalizedFrom = CatalogDomainGuard.Utc(validFromUtc);
        var normalizedUntil = CatalogDomainGuard.Utc(validUntilUtc);
        if (ValidFromUtc == normalizedFrom && ValidUntilUtc == normalizedUntil)
            return;

        ValidFromUtc = normalizedFrom;
        ValidUntilUtc = normalizedUntil;
        TouchRevision(actor, occurredOnUtc);
    }

    public void AssignToUser(string? assignedUserId, string actor, DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        var normalizedUser = CatalogDomainGuard.OptionalOrNull(
            assignedUserId,
            nameof(assignedUserId),
            200);
        if (string.Equals(AssignedUserId, normalizedUser, StringComparison.Ordinal))
            return;

        AssignedUserId = normalizedUser;
        TouchRevision(actor, occurredOnUtc);
    }

    public void ConfigureQuota(
        int? totalLimit,
        int perUserLimit,
        CouponQuotaStrategy quotaStrategy,
        int? bucketCount,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        EnsureDraft();
        var normalizedTotalLimit = ValidateTotalLimit(totalLimit);
        var normalizedPerUserLimit = ValidatePerUserLimit(perUserLimit, normalizedTotalLimit);
        var replacementBuckets = BuildQuotaBuckets(
            Id,
            normalizedTotalLimit,
            quotaStrategy,
            bucketCount);

        TotalLimit = normalizedTotalLimit;
        PerUserLimit = normalizedPerUserLimit;
        QuotaStrategy = quotaStrategy;
        _quotaBuckets.Clear();
        _quotaBuckets.AddRange(replacementBuckets);
        TouchRevision(actor, occurredOnUtc);
    }

    public void ReplaceQuotaBuckets(
        IEnumerable<int> capacities,
        int perUserLimit,
        string actor,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(capacities);
        EnsureDraft();
        var values = capacities.ToArray();
        if (values.Length == 0 || values.Length > 128 || values.Any(capacity => capacity <= 0))
            throw new ArgumentException("Coupon quota requires between 1 and 128 positive-capacity buckets.", nameof(capacities));

        int total;
        try
        {
            total = checked(values.Sum());
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Total coupon quota is too large.", nameof(capacities), exception);
        }

        TotalLimit = ValidateTotalLimit(total);
        PerUserLimit = ValidatePerUserLimit(perUserLimit, TotalLimit);
        QuotaStrategy = values.Length == 1
            ? CouponQuotaStrategy.SingleRow
            : CouponQuotaStrategy.Bucketed;
        _quotaBuckets.Clear();
        _quotaBuckets.AddRange(values.Select((capacity, index) =>
            new CouponQuotaBucket(Id, index, capacity)));
        TouchRevision(actor, occurredOnUtc);
    }

    public void Activate(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CouponStatus.Active)
            return;
        if (Status == CouponStatus.Expired)
            throw new InvalidOperationException("An expired coupon cannot be activated.");

        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        if (utc >= ValidUntilUtc)
            throw new InvalidOperationException("A coupon cannot be activated after its validity window.");

        EnsureQuotaConfigurationIsExact();
        Status = CouponStatus.Active;
        CompleteStatusChange(actor, utc, "Activated");
    }

    public void Disable(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CouponStatus.Disabled)
            return;
        if (Status == CouponStatus.Expired)
            throw new InvalidOperationException("An expired coupon cannot be disabled.");

        Status = CouponStatus.Disabled;
        CompleteStatusChange(actor, occurredOnUtc, "Disabled");
    }

    public void Expire(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CouponStatus.Expired)
            return;
        var utc = CatalogDomainGuard.Utc(occurredOnUtc);
        if (utc < ValidUntilUtc)
            throw new InvalidOperationException("A coupon cannot expire before its validity window ends.");

        Status = CouponStatus.Expired;
        CompleteStatusChange(actor, utc, "Expired");
    }

    public void MarkExpired(string actor, DateTimeOffset occurredOnUtc) =>
        Expire(actor, occurredOnUtc);

    private static string NormalizeAndValidateCode(string code)
    {
        var normalized = CatalogDomainGuard.Required(code, nameof(code), 80).ToUpperInvariant();
        if (normalized.Length < 6)
            throw new ArgumentException("Coupon code must contain at least six characters.", nameof(code));
        return normalized;
    }

    private static int? ValidateTotalLimit(int? totalLimit)
    {
        if (totalLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalLimit), "Total limit must be positive when supplied.");
        return totalLimit;
    }

    private static int ValidatePerUserLimit(int perUserLimit, int? totalLimit)
    {
        if (perUserLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(perUserLimit), "Per-user limit must be positive.");
        if (totalLimit.HasValue && perUserLimit > totalLimit.Value)
            throw new ArgumentOutOfRangeException(nameof(perUserLimit), "Per-user limit cannot exceed total limit.");
        return perUserLimit;
    }

    private static IReadOnlyList<CouponQuotaBucket> BuildQuotaBuckets(
        Guid couponId,
        int? totalLimit,
        CouponQuotaStrategy quotaStrategy,
        int? bucketCount)
    {
        if (!totalLimit.HasValue)
        {
            if (quotaStrategy == CouponQuotaStrategy.Bucketed)
                throw new InvalidOperationException("Bucketed quota requires a finite total limit.");
            if (bucketCount.HasValue)
                throw new InvalidOperationException("An unlimited coupon cannot define quota buckets.");
            return [];
        }

        if (quotaStrategy == CouponQuotaStrategy.SingleRow)
        {
            if (bucketCount is not null and not 1)
                throw new InvalidOperationException("Single-row quota must use exactly one bucket.");
            return [new CouponQuotaBucket(couponId, 0, totalLimit.Value)];
        }

        var count = bucketCount
            ?? throw new InvalidOperationException("Bucketed quota requires an explicit bucket count.");
        if (count is < 2 or > 128)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), "Bucket count must be between 2 and 128.");
        if (count > totalLimit.Value)
            throw new ArgumentOutOfRangeException(nameof(bucketCount), "Every quota bucket must have positive capacity.");

        var baseCapacity = totalLimit.Value / count;
        var remainder = totalLimit.Value % count;
        return Enumerable.Range(0, count)
            .Select(number => new CouponQuotaBucket(
                couponId,
                number,
                baseCapacity + (number < remainder ? 1 : 0)))
            .ToArray();
    }

    private void EnsureQuotaConfigurationIsExact()
    {
        if (!TotalLimit.HasValue)
        {
            if (_quotaBuckets.Count != 0 || QuotaStrategy == CouponQuotaStrategy.Bucketed)
                throw new InvalidOperationException("Unlimited coupon quota configuration is inconsistent.");
            return;
        }

        if (_quotaBuckets.Count == 0
            || _quotaBuckets.Any(bucket => bucket.Capacity <= 0)
            || _quotaBuckets.Select(bucket => bucket.BucketNumber).Distinct().Count() != _quotaBuckets.Count
            || _quotaBuckets.Sum(bucket => bucket.Capacity) != TotalLimit.Value)
        {
            throw new InvalidOperationException("Coupon quota bucket capacity must exactly equal the total limit.");
        }

        if (QuotaStrategy == CouponQuotaStrategy.SingleRow && _quotaBuckets.Count != 1)
            throw new InvalidOperationException("Single-row quota requires exactly one bucket.");
        if (QuotaStrategy == CouponQuotaStrategy.Bucketed && _quotaBuckets.Count < 2)
            throw new InvalidOperationException("Bucketed quota requires at least two buckets.");
    }

    private void EnsureDraft()
    {
        if (Status != CouponStatus.Draft)
            throw new InvalidOperationException("Only a draft coupon can be reconfigured.");
    }

    private void CompleteStatusChange(string actor, DateTimeOffset occurredOnUtc, string change)
    {
        TouchRevision(actor, occurredOnUtc);
        AddDomainEvent(new CouponSnapshotChangedDomainEvent(this, Revision, change));
    }
}

/// <summary>
/// 发布给订单服务的静态配额拓扑；运行时预留和核销计数不属于目录服务状态。
/// </summary>
public sealed class CouponQuotaBucket
{
    private CouponQuotaBucket()
    {
    }

    public CouponQuotaBucket(Guid couponId, int bucketNumber, int capacity)
    {
        CouponId = CatalogDomainGuard.Required(couponId, nameof(couponId));
        if (bucketNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(bucketNumber));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        BucketNumber = bucketNumber;
        Capacity = capacity;
    }

    public Guid CouponId { get; private set; }
    public int BucketNumber { get; private set; }
    public int Capacity { get; private set; }
}
