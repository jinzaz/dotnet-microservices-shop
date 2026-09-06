using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Application.CreateOrder;

namespace MSA.OrderService.Application.CreateFlashSaleOrder;

public sealed record CreateFlashSaleOrderCommand(
    string UserId,
    string IdempotencyKey,
    Guid CampaignId,
    int Quantity,
    string PaymentMethod,
    ShippingAddressInput ShippingAddress,
    IReadOnlyList<string>? CouponCodes = null) : ICommand<Guid>;
