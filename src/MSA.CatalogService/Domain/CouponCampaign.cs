namespace MSA.CatalogService.Domain;

public enum CouponCampaignStatus
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Ended = 3
}

/// <summary>How the validity window of a claimed coupon is decided.</summary>
public enum CouponValidityMode
{
    /// <summary>Every claimed coupon shares one fixed window.</summary>
    Fixed = 0,

    /// <summary>Each coupon is valid for N days from the moment it is claimed.</summary>
    RelativeToClaim = 1
}

public enum CouponClaimRejection
{
    Unavailable,
    NotStarted,
    Ended,
    SoldOut,
    DailySoldOut,
    UserLimitReached,
    UserDailyLimitReached
}

/// <summary>A claim was refused for a business reason the shopper can understand; mapped to 409 with a reason code.</summary>
public sealed class CouponClaimRejectedException(CouponClaimRejection reason, string message)
    : InvalidOperationException(message)
{
    public CouponClaimRejection Reason { get; } = reason;
}

/// <summary>Everything an operator configures on a claim campaign; revised as a whole while it is a draft.</summary>
public sealed record CouponCampaignDefinition(
    string Title,
    string? Description,
    DateTimeOffset ClaimStartsOnUtc,
    DateTimeOffset ClaimEndsOnUtc,
    int? TotalQuantity,
    int? DailyQuantity,
    int PerUserLimit,
    int? PerUserDailyLimit,
    CouponValidityMode ValidityMode,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    int? ValidDays,
    int UsesPerCoupon,
    bool IsListed);

/// <summary>Claim counters read under the campaign row lock, so the limits below are enforced exactly.</summary>
public sealed record CouponClaimCounts(int UserTotal, int UserToday, int CampaignToday);

/// <summary>
/// 领券活动：运营在凭券促销下投放一批可领取的券，用户领取后为其生成一张专属券进入钱包。
/// 领取计数不改变 <see cref="AuditedCatalogAggregateRoot{TId}.Version"/>，后台编辑不会与抢券互相冲突。
/// </summary>
public sealed class CouponCampaign : AuditedCatalogAggregateRoot<Guid>
{
    public const int MaximumQuantity = 10_000_000;
    public const int MaximumPerUserLimit = 100;
    public const int MaximumValidDays = 365;

    private CouponCampaign()
    {
    }

    public CouponCampaign(
        Guid id,
        Guid promotionId,
        CouponCampaignDefinition definition,
        string actor,
        DateTimeOffset createdOnUtc)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        PromotionId = CatalogDomainGuard.Required(promotionId, nameof(promotionId));
        Apply(definition);
        Status = CouponCampaignStatus.Draft;
        InitializeAudit(actor, createdOnUtc);
    }

    public Guid PromotionId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public CouponCampaignStatus Status { get; private set; }
    public DateTimeOffset ClaimStartsOnUtc { get; private set; }
    public DateTimeOffset ClaimEndsOnUtc { get; private set; }
    public int? TotalQuantity { get; private set; }
    public int ClaimedQuantity { get; private set; }
    public int? DailyQuantity { get; private set; }
    public int PerUserLimit { get; private set; }
    public int? PerUserDailyLimit { get; private set; }
    public CouponValidityMode ValidityMode { get; private set; }
    public DateTimeOffset? ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidUntilUtc { get; private set; }
    public int? ValidDays { get; private set; }
    public int UsesPerCoupon { get; private set; }
    public bool IsListed { get; private set; }

    public int? RemainingQuantity => TotalQuantity - ClaimedQuantity;

    public void Revise(CouponCampaignDefinition definition, string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status != CouponCampaignStatus.Draft)
            throw new InvalidOperationException("Only a draft claim campaign can be edited.");
        Apply(definition);
        Touch(actor, occurredOnUtc);
    }

    public void Publish(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status != CouponCampaignStatus.Draft)
            throw new InvalidOperationException("Only a draft claim campaign can be published.");
        EnsureNotOver(occurredOnUtc);
        Status = CouponCampaignStatus.Active;
        Touch(actor, occurredOnUtc);
    }

    public void Pause(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status != CouponCampaignStatus.Active)
            throw new InvalidOperationException("Only an active claim campaign can be paused.");
        Status = CouponCampaignStatus.Paused;
        Touch(actor, occurredOnUtc);
    }

    public void Resume(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status != CouponCampaignStatus.Paused)
            throw new InvalidOperationException("Only a paused claim campaign can be resumed.");
        EnsureNotOver(occurredOnUtc);
        Status = CouponCampaignStatus.Active;
        Touch(actor, occurredOnUtc);
    }

    /// <summary>Stops claiming for good. Coupons already claimed stay valid in shoppers' wallets.</summary>
    public void End(string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CouponCampaignStatus.Ended)
            return;
        Status = CouponCampaignStatus.Ended;
        Touch(actor, occurredOnUtc);
    }

    /// <summary>Adds or trims stock while live; it can never drop below what was already claimed.</summary>
    public void ChangeTotalQuantity(int? totalQuantity, string actor, DateTimeOffset occurredOnUtc)
    {
        if (Status == CouponCampaignStatus.Ended)
            throw new InvalidOperationException("An ended claim campaign cannot change its quantity.");
        ValidateQuantities(totalQuantity, DailyQuantity, PerUserLimit);
        if (totalQuantity < ClaimedQuantity)
            throw new InvalidOperationException($"Total quantity cannot be lower than the {ClaimedQuantity} coupons already claimed.");
        if (TotalQuantity == totalQuantity)
            return;
        TotalQuantity = totalQuantity;
        Touch(actor, occurredOnUtc);
    }

    /// <summary>
    /// Records one claim and returns the validity window of the coupon to issue. Callers must hold the campaign row lock
    /// and pass counts read under it, otherwise the per-user and daily limits could be exceeded by concurrent claims.
    /// </summary>
    public (DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc) Claim(
        CouponClaimCounts counts,
        DateTimeOffset promotionStartsOnUtc,
        DateTimeOffset promotionEndsOnUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var now = CatalogDomainGuard.Utc(nowUtc);
        if (Status != CouponCampaignStatus.Active)
            throw new CouponClaimRejectedException(CouponClaimRejection.Unavailable, "This claim campaign is not open.");
        if (now < ClaimStartsOnUtc)
            throw new CouponClaimRejectedException(CouponClaimRejection.NotStarted, "Claiming has not started yet.");
        if (now >= ClaimEndsOnUtc || now >= promotionEndsOnUtc)
            throw new CouponClaimRejectedException(CouponClaimRejection.Ended, "Claiming has ended.");
        if (TotalQuantity.HasValue && ClaimedQuantity >= TotalQuantity.Value)
            throw new CouponClaimRejectedException(CouponClaimRejection.SoldOut, "All coupons have been claimed.");
        if (counts.UserTotal >= PerUserLimit)
            throw new CouponClaimRejectedException(CouponClaimRejection.UserLimitReached, "You have reached the claim limit for this coupon.");
        if (PerUserDailyLimit.HasValue && counts.UserToday >= PerUserDailyLimit.Value)
            throw new CouponClaimRejectedException(CouponClaimRejection.UserDailyLimitReached, "You have reached today's claim limit for this coupon.");
        if (DailyQuantity.HasValue && counts.CampaignToday >= DailyQuantity.Value)
            throw new CouponClaimRejectedException(CouponClaimRejection.DailySoldOut, "Today's coupons have all been claimed.");

        var window = ValidityMode == CouponValidityMode.Fixed
            ? (ValidFromUtc!.Value, ValidUntilUtc!.Value)
            : (Max(now, promotionStartsOnUtc), Min(now.AddDays(ValidDays!.Value), promotionEndsOnUtc));
        if (window.Item2 <= now || window.Item1 >= window.Item2)
            throw new CouponClaimRejectedException(CouponClaimRejection.Ended, "The coupon would already be expired.");

        checked
        {
            ClaimedQuantity++;
        }
        return window;
    }

    private void Apply(CouponCampaignDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        CatalogDomainGuard.ValidWindow(
            definition.ClaimStartsOnUtc,
            definition.ClaimEndsOnUtc,
            nameof(definition.ClaimStartsOnUtc),
            nameof(definition.ClaimEndsOnUtc));
        ValidateQuantities(definition.TotalQuantity, definition.DailyQuantity, definition.PerUserLimit);
        if (definition.PerUserDailyLimit is { } perUserDaily && (perUserDaily <= 0 || perUserDaily > definition.PerUserLimit))
            throw new ArgumentOutOfRangeException(nameof(definition.PerUserDailyLimit), "Per-user daily limit must be between 1 and the per-user limit.");
        if (definition.UsesPerCoupon is <= 0 or > MaximumPerUserLimit)
            throw new ArgumentOutOfRangeException(nameof(definition.UsesPerCoupon), $"Uses per coupon must be between 1 and {MaximumPerUserLimit}.");

        if (definition.ValidityMode == CouponValidityMode.Fixed)
        {
            if (definition.ValidFromUtc is not { } from || definition.ValidUntilUtc is not { } until || definition.ValidDays.HasValue)
                throw new ArgumentException("Fixed validity requires a start and end time and no day count.", nameof(definition));
            CatalogDomainGuard.ValidWindow(from, until, nameof(definition.ValidFromUtc), nameof(definition.ValidUntilUtc));
            if (CatalogDomainGuard.Utc(until) <= CatalogDomainGuard.Utc(definition.ClaimStartsOnUtc))
                throw new ArgumentException("Coupons must still be valid after claiming starts.", nameof(definition));
        }
        else if (definition.ValidityMode == CouponValidityMode.RelativeToClaim)
        {
            if (definition.ValidDays is not { } days || days is <= 0 or > MaximumValidDays
                || definition.ValidFromUtc.HasValue || definition.ValidUntilUtc.HasValue)
                throw new ArgumentException($"Relative validity requires between 1 and {MaximumValidDays} days and no fixed window.", nameof(definition));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(definition.ValidityMode));
        }

        Title = CatalogDomainGuard.Required(definition.Title, nameof(definition.Title), 60);
        Description = CatalogDomainGuard.OptionalOrNull(definition.Description, nameof(definition.Description), 200);
        ClaimStartsOnUtc = CatalogDomainGuard.Utc(definition.ClaimStartsOnUtc);
        ClaimEndsOnUtc = CatalogDomainGuard.Utc(definition.ClaimEndsOnUtc);
        TotalQuantity = definition.TotalQuantity;
        DailyQuantity = definition.DailyQuantity;
        PerUserLimit = definition.PerUserLimit;
        PerUserDailyLimit = definition.PerUserDailyLimit;
        ValidityMode = definition.ValidityMode;
        ValidFromUtc = definition.ValidFromUtc is { } validFrom ? CatalogDomainGuard.Utc(validFrom) : null;
        ValidUntilUtc = definition.ValidUntilUtc is { } validUntil ? CatalogDomainGuard.Utc(validUntil) : null;
        ValidDays = definition.ValidDays;
        UsesPerCoupon = definition.UsesPerCoupon;
        IsListed = definition.IsListed;
    }

    private static void ValidateQuantities(int? total, int? daily, int perUserLimit)
    {
        if (total is <= 0 or > MaximumQuantity)
            throw new ArgumentOutOfRangeException(nameof(total), $"Total quantity must be between 1 and {MaximumQuantity} when supplied.");
        if (daily is <= 0 or > MaximumQuantity)
            throw new ArgumentOutOfRangeException(nameof(daily), $"Daily quantity must be between 1 and {MaximumQuantity} when supplied.");
        if (total.HasValue && daily > total)
            throw new ArgumentOutOfRangeException(nameof(daily), "Daily quantity cannot exceed the total quantity.");
        if (perUserLimit is <= 0 or > MaximumPerUserLimit)
            throw new ArgumentOutOfRangeException(nameof(perUserLimit), $"Per-user limit must be between 1 and {MaximumPerUserLimit}.");
        if (total.HasValue && perUserLimit > total.Value)
            throw new ArgumentOutOfRangeException(nameof(perUserLimit), "Per-user limit cannot exceed the total quantity.");
    }

    private void EnsureNotOver(DateTimeOffset occurredOnUtc)
    {
        if (CatalogDomainGuard.Utc(occurredOnUtc) >= ClaimEndsOnUtc)
            throw new InvalidOperationException("The claim window has already ended.");
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}

/// <summary>One successful claim: who claimed which campaign and the personal coupon they received.</summary>
public sealed class CouponClaim
{
    private CouponClaim()
    {
    }

    public CouponClaim(Guid id, Guid campaignId, string userId, Guid couponId, DateTimeOffset claimedOnUtc, DateOnly claimDay)
    {
        Id = CatalogDomainGuard.Required(id, nameof(id));
        CampaignId = CatalogDomainGuard.Required(campaignId, nameof(campaignId));
        UserId = CatalogDomainGuard.Required(userId, nameof(userId), 200);
        CouponId = CatalogDomainGuard.Required(couponId, nameof(couponId));
        ClaimedOnUtc = CatalogDomainGuard.Utc(claimedOnUtc);
        ClaimDay = claimDay;
    }

    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid CouponId { get; private set; }
    public DateTimeOffset ClaimedOnUtc { get; private set; }

    /// <summary>Business-calendar day of the claim, used for daily limits.</summary>
    public DateOnly ClaimDay { get; private set; }
}
