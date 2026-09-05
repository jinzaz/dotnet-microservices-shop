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
            .Must(items => items.Select(item => item.Sku).Distinct(StringComparer.Ordinal).Count() == items.Count)
            .WithMessage("Duplicate SKU lines are not supported.");
        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.Sku).NotEmpty().MaximumLength(80);
            item.RuleFor(value => value.Quantity).InclusiveBetween(1, 999);
        });
        RuleFor(command => command.PaymentMethod).Must(value => value is "Alipay" or "WeChatPay");
    }
}
