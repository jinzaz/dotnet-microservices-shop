using MSA.Infrastrucute.Mediator;

namespace MSA.OrderService.Application.CreateFlashSaleOrder;

public sealed record CreateFlashSaleOrderCommand(
    string UserId,
    string IdempotencyKey,
    Guid CampaignId,
    int Quantity,
    string PaymentMethod) : ICommand<Guid>;
