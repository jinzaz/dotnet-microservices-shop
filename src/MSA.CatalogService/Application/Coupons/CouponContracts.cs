using FluentValidation;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record CreateCouponCommand(
    Guid PromotionId,
    string Code,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string? AssignedUserId,
    int? TotalLimit,
    int PerUserLimit,
    string QuotaStrategy,
    int? BucketCount,
    string Actor) : ICommand<Guid>;

public sealed record ActivateCouponCommand(
    Guid CouponId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record DisableCouponCommand(
    Guid CouponId,
    string? Reason,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ExpireCouponCommand(
    Guid CouponId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

/// <summary>
/// Issues many coupons at once. With <paramref name="AssignedUserIds"/> one personal coupon is issued per user
/// (landing in their wallet); otherwise <paramref name="Count"/> shareable codes are generated.
/// Plaintext codes are returned exactly once because only their digest is stored.
/// </summary>
public sealed record CreateCouponBatchCommand(
    Guid PromotionId,
    int Count,
    IReadOnlyList<string>? AssignedUserIds,
    string? CodePrefix,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    int? TotalLimitPerCoupon,
    int PerUserLimit,
    bool Activate,
    string Actor) : ICommand<IReadOnlyList<IssuedCouponDto>>;

public sealed record IssuedCouponDto(Guid Id, string Code, string MaskedCode, string? AssignedUserId);

public sealed record GetCouponQuery(Guid CouponId) : IQuery<CouponDto?>;

public sealed record ListCouponsQuery(int Skip, int Take, string? Status, Guid? PromotionId, Guid? CampaignId = null)
    : IQuery<PagedResult<CouponDto>>;

public sealed class CreateCouponBatchValidator : AbstractValidator<CreateCouponBatchCommand>
{
    public const int MaximumBatchSize = 500;

    public CreateCouponBatchValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Count).InclusiveBetween(1, MaximumBatchSize)
            .When(command => command.AssignedUserIds is null || command.AssignedUserIds.Count == 0);
        RuleFor(command => command.AssignedUserIds!.Count).InclusiveBetween(1, MaximumBatchSize)
            .When(command => command.AssignedUserIds is { Count: > 0 });
        RuleForEach(command => command.AssignedUserIds).NotEmpty().MaximumLength(200);
        RuleFor(command => command.AssignedUserIds)
            .Must(ids => ids is null || ids.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Assigned users must be unique.");
        RuleFor(command => command.CodePrefix).MaximumLength(20).Matches("^[A-Za-z0-9\\-]*$");
        RuleFor(command => command.TotalLimitPerCoupon).GreaterThan(0).When(command => command.TotalLimitPerCoupon.HasValue);
        RuleFor(command => command.PerUserLimit).GreaterThan(0);
        RuleFor(command => command).Must(command =>
                !command.TotalLimitPerCoupon.HasValue || command.PerUserLimit <= command.TotalLimitPerCoupon.Value)
            .WithMessage("Per-user limit cannot exceed total limit.");
        RuleFor(command => command).Must(command => command.ValidFromUtc < command.ValidUntilUtc)
            .WithMessage("Coupon validity end must be after its start.");
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateCouponValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MinimumLength(6).MaximumLength(80);
        RuleFor(command => command.AssignedUserId).MaximumLength(200);
        RuleFor(command => command.TotalLimit).GreaterThan(0).When(command => command.TotalLimit.HasValue);
        RuleFor(command => command.PerUserLimit).GreaterThan(0);
        RuleFor(command => command.QuotaStrategy)
            .Must(value => Enum.TryParse<Domain.CouponQuotaStrategy>(value, true, out _))
            .WithMessage("QuotaStrategy must be SingleRow or Bucketed.");
        RuleFor(command => command.BucketCount).InclusiveBetween(2, 128)
            .When(command => command.BucketCount.HasValue);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
        RuleFor(command => command).Must(command => command.ValidFromUtc < command.ValidUntilUtc)
            .WithMessage("Coupon validity end must be after its start.");
        RuleFor(command => command).Must(command =>
                !command.TotalLimit.HasValue || command.PerUserLimit <= command.TotalLimit.Value)
            .WithMessage("Per-user limit cannot exceed total limit.");
        RuleFor(command => command).Must(command =>
                !string.Equals(command.QuotaStrategy, "Bucketed", StringComparison.OrdinalIgnoreCase)
                || command.TotalLimit.HasValue && command.BucketCount.HasValue)
            .WithMessage("Bucketed quota requires a finite total limit and an explicit bucket count.");
    }
}
