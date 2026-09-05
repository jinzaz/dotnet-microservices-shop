using FluentValidation;

namespace MSA.OrderService.Application.CreateFlashSaleOrder;

public sealed class CreateFlashSaleOrderCommandValidator : AbstractValidator<CreateFlashSaleOrderCommand>
{
    public CreateFlashSaleOrderCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty().MaximumLength(200);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.CampaignId).NotEmpty();
        RuleFor(command => command.Quantity).InclusiveBetween(1, 999);
        RuleFor(command => command.PaymentMethod).Must(value => value is "Alipay" or "WeChatPay");
        RuleFor(command => command.CouponCodes)
            .Must(codes => codes is null || codes.Count <= 5)
            .WithMessage("At most five coupon codes can be applied.")
            .Must(codes => codes is null
                || codes.Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .Count() == codes.Count)
            .WithMessage("Coupon codes must be non-empty and unique.");
        RuleForEach(command => command.CouponCodes)
            .NotEmpty()
            .MaximumLength(80)
            .When(command => command.CouponCodes is not null);
    }
}
