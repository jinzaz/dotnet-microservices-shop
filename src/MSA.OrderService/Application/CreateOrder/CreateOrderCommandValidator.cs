using FluentValidation;

namespace MSA.OrderService.Application.CreateOrder;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty().MaximumLength(200);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Items).NotEmpty().Must(items => items.Count <= 100);
        RuleFor(command => command.Items)
            .Must(items => items.Select(item => item.Sku?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count)
            .WithMessage("Duplicate SKU lines are not supported.");
        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.Sku).NotEmpty().MaximumLength(80);
            item.RuleFor(value => value.Quantity).InclusiveBetween(1, 999);
            item.RuleFor(value => value.OfferId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("OfferId cannot be an empty GUID.");
        });
        RuleFor(command => command.PaymentMethod).Must(value => value is "Alipay" or "WeChatPay");
        RuleFor(command => command.ShippingAddress)
            .NotNull()
            .SetValidator(new ShippingAddressInputValidator());
        RuleFor(command => command)
            .Must(command => command.SourceBasketId.HasValue == command.SourceBasketVersion.HasValue)
            .WithMessage("SourceBasketId and SourceBasketVersion must be provided together.");
        RuleFor(command => command.SourceBasketId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("SourceBasketId cannot be an empty GUID.");
        RuleFor(command => command.SourceBasketVersion)
            .GreaterThanOrEqualTo(0)
            .When(command => command.SourceBasketVersion.HasValue);
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
        RuleFor(command => command.WalletCouponIds)
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count)
            .WithMessage("Wallet coupon ids must be non-empty and unique.");
        RuleFor(command => command)
            .Must(command => (command.CouponCodes?.Count ?? 0) + (command.WalletCouponIds?.Count ?? 0) <= 5)
            .WithMessage("At most five coupons can be applied.");
    }
}

public sealed class ShippingAddressInputValidator : AbstractValidator<ShippingAddressInput>
{
    public ShippingAddressInputValidator()
    {
        RuleFor(address => address.AddressId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("AddressId cannot be an empty GUID.");
        RuleFor(address => address.RecipientName).NotEmpty().MaximumLength(100);
        RuleFor(address => address.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(address => address.CountryCode).NotEmpty().Length(2, 3);
        RuleFor(address => address.Province).NotEmpty().MaximumLength(100);
        RuleFor(address => address.City).NotEmpty().MaximumLength(100);
        RuleFor(address => address.District).NotEmpty().MaximumLength(100);
        RuleFor(address => address.StreetLine1).NotEmpty().MaximumLength(300);
        RuleFor(address => address.StreetLine2).MaximumLength(300);
        RuleFor(address => address.PostalCode).MaximumLength(20);
    }
}
