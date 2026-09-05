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
    }
}
