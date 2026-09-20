using FluentValidation;
using MSA.CatalogService.Domain;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

/// <summary>Operator-facing campaign settings; <see cref="ValidityMode"/> is "Fixed" or "RelativeToClaim".</summary>
public sealed record CouponCampaignInput(
    string Title,
    string? Description,
    DateTimeOffset ClaimStartsOnUtc,
    DateTimeOffset ClaimEndsOnUtc,
    int? TotalQuantity,
    int? DailyQuantity,
    int PerUserLimit,
    int? PerUserDailyLimit,
    string ValidityMode,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    int? ValidDays,
    int UsesPerCoupon,
    bool IsListed);

public enum CouponCampaignAction
{
    Publish,
    Pause,
    Resume,
    End
}

public sealed record CreateCouponCampaignCommand(
    Guid PromotionId,
    CouponCampaignInput Definition,
    string Actor) : ICommand<Guid>;

public sealed record ReviseCouponCampaignCommand(
    Guid CampaignId,
    CouponCampaignInput Definition,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ChangeCouponCampaignStatusCommand(
    Guid CampaignId,
    CouponCampaignAction Action,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

/// <summary>Adds or trims stock of a live campaign; <see cref="TotalQuantity"/> null means unlimited.</summary>
public sealed record ChangeCouponCampaignQuantityCommand(
    Guid CampaignId,
    int? TotalQuantity,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

/// <summary>A signed-in shopper claims one coupon; a personal coupon is issued into their wallet.</summary>
public sealed record ClaimCouponCommand(Guid CampaignId, string UserId) : ICommand<CouponClaimResultDto>;

public sealed record ListCouponCampaignsQuery(int Skip, int Take, string? Status, Guid? PromotionId)
    : IQuery<PagedResult<CouponCampaignDto>>;

public sealed record GetCouponCampaignQuery(Guid CampaignId) : IQuery<CouponCampaignDto?>;

public sealed record GetCouponCampaignStatsQuery(Guid CampaignId, int Days) : IQuery<CouponCampaignStatsDto>;

public sealed record ListCouponClaimsQuery(Guid CampaignId, int Skip, int Take) : IQuery<PagedResult<CouponClaimDto>>;

/// <summary>Campaigns shown in the storefront coupon center; <see cref="UserId"/> adds the shopper's own claim counts.</summary>
public sealed record ListClaimableCouponCampaignsQuery(string? UserId) : IQuery<IReadOnlyList<StorefrontCouponCampaignDto>>;

/// <summary>One campaign by id, including unlisted ones shared by link.</summary>
public sealed record GetStorefrontCouponCampaignQuery(Guid CampaignId, string? UserId) : IQuery<StorefrontCouponCampaignDto?>;

public sealed record CouponCampaignDto(
    Guid Id,
    Guid PromotionId,
    string Title,
    string? Description,
    string Status,
    DateTimeOffset ClaimStartsOnUtc,
    DateTimeOffset ClaimEndsOnUtc,
    int? TotalQuantity,
    int ClaimedQuantity,
    int? DailyQuantity,
    int PerUserLimit,
    int? PerUserDailyLimit,
    string ValidityMode,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    int? ValidDays,
    int UsesPerCoupon,
    bool IsListed,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset UpdatedOnUtc,
    string CreatedBy,
    string UpdatedBy,
    long Version);

public sealed record CouponClaimDayDto(DateOnly Day, int Count);

public sealed record CouponCampaignStatsDto(
    int ClaimedQuantity,
    int ClaimedToday,
    int ClaimUsers,
    IReadOnlyList<CouponClaimDayDto> Daily);

public sealed record CouponClaimDto(Guid Id, string UserId, Guid CouponId, DateTimeOffset ClaimedOnUtc);

public sealed record CouponClaimResultDto(
    Guid CouponId,
    string MaskedCode,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    int? RemainingQuantity,
    int MyClaimedCount,
    int PerUserLimit);

public static class CouponClaimStates
{
    public const string NotStarted = "NotStarted";
    public const string Claimable = "Claimable";
    public const string SoldOut = "SoldOut";
    public const string DailySoldOut = "DailySoldOut";
    public const string LimitReached = "LimitReached";
    public const string DailyLimitReached = "DailyLimitReached";
    public const string Ended = "Ended";
}

public sealed record StorefrontCouponCampaignDto(
    Guid Id,
    string Title,
    string? Description,
    Guid PromotionId,
    string PromotionName,
    IReadOnlyList<StorefrontPromotionRuleDto> Rules,
    IReadOnlyList<StorefrontPromotionBenefitDto> Benefits,
    IReadOnlyList<StorefrontPromotionTargetDto> Targets,
    DateTimeOffset ClaimStartsOnUtc,
    DateTimeOffset ClaimEndsOnUtc,
    int? TotalQuantity,
    int ClaimedQuantity,
    int? DailyQuantity,
    int ClaimedToday,
    int PerUserLimit,
    int? PerUserDailyLimit,
    int MyClaimedCount,
    int MyClaimedToday,
    string ValidityMode,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    int? ValidDays,
    int UsesPerCoupon,
    string ClaimState);

public sealed class CouponCampaignInputValidator : AbstractValidator<CouponCampaignInput>
{
    public CouponCampaignInputValidator()
    {
        RuleFor(input => input.Title).NotEmpty().MaximumLength(60);
        RuleFor(input => input.Description).MaximumLength(200);
        RuleFor(input => input).Must(input => input.ClaimStartsOnUtc < input.ClaimEndsOnUtc)
            .WithMessage("Claiming must end after it starts.");
        RuleFor(input => input.TotalQuantity).InclusiveBetween(1, CouponCampaign.MaximumQuantity).When(input => input.TotalQuantity.HasValue);
        RuleFor(input => input.DailyQuantity).InclusiveBetween(1, CouponCampaign.MaximumQuantity).When(input => input.DailyQuantity.HasValue);
        RuleFor(input => input.PerUserLimit).InclusiveBetween(1, CouponCampaign.MaximumPerUserLimit);
        RuleFor(input => input.UsesPerCoupon).InclusiveBetween(1, CouponCampaign.MaximumPerUserLimit);
        RuleFor(input => input.ValidityMode)
            .Must(value => Enum.TryParse<CouponValidityMode>(value, true, out _))
            .WithMessage("ValidityMode must be Fixed or RelativeToClaim.");
        RuleFor(input => input.ValidDays).InclusiveBetween(1, CouponCampaign.MaximumValidDays).When(input => input.ValidDays.HasValue);
    }
}

public sealed class CreateCouponCampaignValidator : AbstractValidator<CreateCouponCampaignCommand>
{
    public CreateCouponCampaignValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Definition).NotNull().SetValidator(new CouponCampaignInputValidator());
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class ReviseCouponCampaignValidator : AbstractValidator<ReviseCouponCampaignCommand>
{
    public ReviseCouponCampaignValidator()
    {
        RuleFor(command => command.CampaignId).NotEmpty();
        RuleFor(command => command.Definition).NotNull().SetValidator(new CouponCampaignInputValidator());
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}
