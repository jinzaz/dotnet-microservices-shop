using MSA.Grpc.Contracts.Catalog;
using MSA.OrderService.Domain;

namespace MSA.OrderService.Application.Realtime;

/// <summary>
/// 目录确认后的价格直接变成定价引擎认识的形状，让确认过的价格参与同一套计算，而不是另起一套分支。
/// 确认本身（<see cref="ICatalogPriceAuthority"/>）由购物车与订单共用，所以只有这段转换留在订单里。
/// </summary>
public static class ConfirmedSkuPriceExtensions
{
    public static OrderProductProjection ToPricingSnapshot(
        this ConfirmedSkuPrice confirmed,
        DateTimeOffset effectiveOnUtc) => new(
        confirmed.VariantId,
        confirmed.ProductId,
        confirmed.Sku,
        confirmed.ProductName,
        confirmed.VariantName,
        confirmed.AttributesJson,
        confirmed.Price,
        confirmed.Currency,
        confirmed.IsActive,
        confirmed.CategoryId,
        confirmed.Revision,
        effectiveOnUtc);
}

public sealed record AvailabilityRequestLine(string Sku, int Quantity, Guid? OfferId);

public sealed record AvailabilityLine(string Sku, int Available, bool Sufficient, string? Reason);

public sealed record AvailabilityCheck(bool AllAvailable, IReadOnlyList<AvailabilityLine> Lines);

/// <summary>库存的实时可售查询；返回 null 表示这次问不到。</summary>
public interface IInventoryAvailabilityGateway
{
    Task<AvailabilityCheck?> CheckAsync(
        IReadOnlyList<AvailabilityRequestLine> lines,
        string userId,
        CancellationToken cancellationToken);
}

public sealed record PaymentStatusSnapshot(
    Guid PaymentId,
    string Status,
    decimal Amount,
    string Currency,
    string Channel,
    string? PaymentUrl,
    DateTimeOffset? ExpiresOnUtc);

/// <summary>支付的实时状态查询；返回 null 表示没有支付单或这次问不到。</summary>
public interface IPaymentStatusGateway
{
    Task<PaymentStatusSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed record ConfirmedCouponState(Guid CouponId, bool Usable, string? Reason);

public sealed record CouponConfirmation(IReadOnlyDictionary<Guid, ConfirmedCouponState> Items);

/// <summary>
/// 券的实时状态权威（目录）。券投影是最终一致的，作废与改期变化快；返回 null 表示这次问不到，
/// 此时仍由本地的领用记录把关配额与限领。
/// </summary>
public interface ICouponAuthority
{
    Task<CouponConfirmation?> ConfirmAsync(
        IReadOnlyCollection<Guid> couponIds,
        string userId,
        CancellationToken cancellationToken);
}

public sealed record AuthoritativeAddress(
    string RecipientName,
    string PhoneNumber,
    string CountryCode,
    string Province,
    string City,
    string District,
    string Street,
    string? PostalCode);

/// <param name="Found">地址不存在或不属于该用户时为 false —— 对下单来说是同一个结论。</param>
public sealed record AddressLookup(bool Found, AuthoritativeAddress? Address);

/// <summary>收货地址的权威来源（用户服务）；返回 null 表示这次问不到。</summary>
public interface IUserAddressAuthority
{
    Task<AddressLookup?> GetAsync(string userId, Guid addressId, CancellationToken cancellationToken);
}
