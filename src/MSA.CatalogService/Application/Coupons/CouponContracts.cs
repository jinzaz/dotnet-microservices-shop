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

public sealed record GetCouponQuery(Guid CouponId) : IQuery<CouponDto?>;

public sealed record ListCouponsQuery(int Skip, int Take, string? Status, Guid? PromotionId)
    : IQuery<PagedResult<CouponDto>>;

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
