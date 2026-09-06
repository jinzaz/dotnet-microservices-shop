using FluentValidation;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.ManageFulfillment;

public sealed class UpdateOrderFulfillmentCommandValidator : AbstractValidator<UpdateOrderFulfillmentCommand>
{
    public UpdateOrderFulfillmentCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
        RuleFor(command => command.TargetStatus)
            .Must(status => status is OrderFulfillmentStatus.Shipped
                or OrderFulfillmentStatus.InTransit
                or OrderFulfillmentStatus.Delivered)
            .WithMessage("Only shipping, in-transit and delivered transitions are supported by this command.");
        RuleFor(command => command.CarrierCode)
            .NotEmpty()
            .MaximumLength(50)
            .When(command => command.TargetStatus == OrderFulfillmentStatus.Shipped);
        RuleFor(command => command.CarrierName)
            .NotEmpty()
            .MaximumLength(120)
            .When(command => command.TargetStatus == OrderFulfillmentStatus.Shipped);
        RuleFor(command => command.TrackingNumber)
            .NotEmpty()
            .MaximumLength(120)
            .When(command => command.TargetStatus == OrderFulfillmentStatus.Shipped);
        RuleFor(command => command.Note).MaximumLength(1000);
    }
}
