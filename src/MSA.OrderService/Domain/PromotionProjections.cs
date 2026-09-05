namespace MSA.OrderService.Domain;

public sealed class PromotionProjection
{
    private PromotionProjection()
    {
    }

    public PromotionProjection(
        Guid id,
        string code,
        string name,
        string triggerType,
        string status,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int priority,
        string stackPolicy,
        string? exclusiveGroup,
        long sourceRevision,
        string definitionChecksum,
        string rulesJson,
        string benefitsJson,
        string targetsJson,
        DateTimeOffset updatedOnUtc)
    {
        Id = id;
        Apply(code, name, triggerType, status, startsOnUtc, endsOnUtc, priority,
            stackPolicy, exclusiveGroup, sourceRevision, definitionChecksum,
            rulesJson, benefitsJson, targetsJson, updatedOnUtc);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TriggerType { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset StartsOnUtc { get; private set; }
    public DateTimeOffset EndsOnUtc { get; private set; }
    public int Priority { get; private set; }
    public string StackPolicy { get; private set; } = string.Empty;
    public string? ExclusiveGroup { get; private set; }
    public long SourceRevision { get; private set; }
    public string DefinitionChecksum { get; private set; } = string.Empty;
    public string RulesJson { get; private set; } = "[]";
    public string BenefitsJson { get; private set; } = "[]";
    public string TargetsJson { get; private set; } = "[]";
    public DateTimeOffset UpdatedOnUtc { get; private set; }

    public bool IsEffective(DateTimeOffset now) =>
        string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase)
        && StartsOnUtc <= now
        && now < EndsOnUtc;

    public bool Apply(
        string code,
        string name,
        string triggerType,
        string status,
        DateTimeOffset startsOnUtc,
        DateTimeOffset endsOnUtc,
        int priority,
        string stackPolicy,
        string? exclusiveGroup,
        long sourceRevision,
        string definitionChecksum,
        string rulesJson,
        string benefitsJson,
        string targetsJson,
        DateTimeOffset updatedOnUtc)
    {
        if (sourceRevision < SourceRevision
            || sourceRevision == SourceRevision && updatedOnUtc <= UpdatedOnUtc)
        {
            return false;
        }

        Code = code;
        Name = name;
        TriggerType = triggerType;
        Status = status;
        StartsOnUtc = startsOnUtc;
        EndsOnUtc = endsOnUtc;
        Priority = priority;
        StackPolicy = stackPolicy;
        ExclusiveGroup = exclusiveGroup;
        SourceRevision = sourceRevision;
        DefinitionChecksum = definitionChecksum;
        RulesJson = rulesJson;
        BenefitsJson = benefitsJson;
        TargetsJson = targetsJson;
        UpdatedOnUtc = updatedOnUtc;
        return true;
    }
}

public sealed class CouponProjection
{
    private readonly List<CouponQuotaBucket> _quotaBuckets = [];

    private CouponProjection()
    {
    }

    public CouponProjection(
        Guid id,
        Guid promotionId,
        string codeDigest,
        string maskedCode,
        string status,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string? assignedUserId,
        int? totalLimit,
        int perUserLimit,
        string quotaStrategy,
        long sourceRevision,
        IEnumerable<CouponQuotaBucket> quotaBuckets,
        DateTimeOffset updatedOnUtc)
    {
        Id = id;
        PromotionId = promotionId;
        CodeDigest = codeDigest;
        Apply(maskedCode, status, validFromUtc, validUntilUtc, assignedUserId,
            totalLimit, perUserLimit, quotaStrategy, sourceRevision, updatedOnUtc);
        _quotaBuckets.AddRange(quotaBuckets);
    }

    public Guid Id { get; private set; }
    public Guid PromotionId { get; private set; }
    public string CodeDigest { get; private set; } = string.Empty;
    public string MaskedCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset ValidUntilUtc { get; private set; }
    public string? AssignedUserId { get; private set; }
    public int? TotalLimit { get; private set; }
    public int PerUserLimit { get; private set; }
    public string QuotaStrategy { get; private set; } = string.Empty;
    public long SourceRevision { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public IReadOnlyCollection<CouponQuotaBucket> QuotaBuckets => _quotaBuckets;

    public bool IsAvailableFor(string userId, DateTimeOffset now) =>
        string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase)
        && ValidFromUtc <= now
        && now < ValidUntilUtc
        && (AssignedUserId is null || string.Equals(AssignedUserId, userId, StringComparison.Ordinal));

    public bool Apply(
        string maskedCode,
        string status,
        DateTimeOffset validFromUtc,
        DateTimeOffset validUntilUtc,
        string? assignedUserId,
        int? totalLimit,
        int perUserLimit,
        string quotaStrategy,
        long sourceRevision,
        DateTimeOffset updatedOnUtc)
    {
        if (sourceRevision < SourceRevision
            || sourceRevision == SourceRevision && updatedOnUtc <= UpdatedOnUtc)
        {
            return false;
        }

        MaskedCode = maskedCode;
        Status = status;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        AssignedUserId = assignedUserId;
        TotalLimit = totalLimit;
        PerUserLimit = perUserLimit;
        QuotaStrategy = quotaStrategy;
        SourceRevision = sourceRevision;
        UpdatedOnUtc = updatedOnUtc;
        return true;
    }
}

public sealed class CouponQuotaBucket
{
    private CouponQuotaBucket()
    {
    }

    public CouponQuotaBucket(Guid couponId, int bucketNumber, int capacity)
    {
        if (couponId == Guid.Empty)
            throw new ArgumentException("Coupon id is required.", nameof(couponId));
        if (bucketNumber < 0 || capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        CouponId = couponId;
        BucketNumber = bucketNumber;
        Capacity = capacity;
    }

    public long Id { get; private set; }
    public Guid CouponId { get; private set; }
    public int BucketNumber { get; private set; }
    public int Capacity { get; private set; }
    public int ReservedCount { get; private set; }
    public int ConsumedCount { get; private set; }
    public long Version { get; private set; }
}

public sealed class OrderCouponRedemption
{
    private OrderCouponRedemption()
    {
    }

    public OrderCouponRedemption(
        Guid id,
        Guid orderId,
        Guid couponId,
        string userId,
        long? quotaBucketId,
        string maskedCode,
        decimal discountAmount,
        DateTimeOffset expiresOnUtc,
        DateTimeOffset createdOnUtc)
    {
        Id = id;
        OrderId = orderId;
        CouponId = couponId;
        UserId = userId;
        QuotaBucketId = quotaBucketId;
        MaskedCode = maskedCode;
        DiscountAmount = Money.Round(discountAmount);
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
        Status = CouponRedemptionStatus.Reserved;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CouponId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public long? QuotaBucketId { get; private set; }
    public string MaskedCode { get; private set; } = string.Empty;
    public decimal DiscountAmount { get; private set; }
    public CouponRedemptionStatus Status { get; private set; }
    public DateTimeOffset ExpiresOnUtc { get; private set; }
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public DateTimeOffset UpdatedOnUtc { get; private set; }
    public long Version { get; private set; }

    public bool Consume(DateTimeOffset now)
    {
        if (Status == CouponRedemptionStatus.Consumed)
            return false;
        if (Status != CouponRedemptionStatus.Reserved)
            throw new InvalidOperationException("Only a reserved coupon can be consumed.");
        Status = CouponRedemptionStatus.Consumed;
        UpdatedOnUtc = now;
        Version++;
        return true;
    }

    public bool Release(DateTimeOffset now)
    {
        if (Status == CouponRedemptionStatus.Released)
            return false;
        if (Status != CouponRedemptionStatus.Reserved)
            return false;
        Status = CouponRedemptionStatus.Released;
        UpdatedOnUtc = now;
        Version++;
        return true;
    }
}

public enum CouponRedemptionStatus
{
    Reserved = 0,
    Consumed = 1,
    Released = 2
}
