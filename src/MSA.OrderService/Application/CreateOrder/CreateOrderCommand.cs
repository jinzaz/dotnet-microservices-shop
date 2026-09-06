using MSA.Infrastrucute.Mediator;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.CreateOrder;

public sealed record CreateOrderItem(string Sku, int Quantity);

public sealed record ShippingAddressInput(
    Guid? AddressId,
    string RecipientName,
    string PhoneNumber,
    string CountryCode,
    string Province,
    string City,
    string District,
    string StreetLine1,
    string? StreetLine2,
    string? PostalCode)
{
    public ShippingAddressSnapshot ToSnapshot() =>
        new(
            AddressId,
            RecipientName,
            PhoneNumber,
            CountryCode,
            Province,
            City,
            District,
            StreetLine1,
            StreetLine2,
            PostalCode);
}

public sealed record CreateOrderCommand(
    string UserId,
    string IdempotencyKey,
    IReadOnlyList<CreateOrderItem> Items,
    string PaymentMethod,
    ShippingAddressInput ShippingAddress,
    IReadOnlyList<string>? CouponCodes = null,
    Guid? SourceBasketId = null,
    long? SourceBasketVersion = null) : ICommand<Guid>;
