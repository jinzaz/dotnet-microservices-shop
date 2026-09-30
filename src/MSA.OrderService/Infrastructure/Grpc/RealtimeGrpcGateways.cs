using Microsoft.Extensions.Logging;
using MSA.Grpc;
using MSA.Grpc.Contracts.Catalog;
using MSA.Grpc.Contracts.Inventory;
using MSA.Grpc.Contracts.Payment;
using MSA.Grpc.Contracts.User;
using MSA.OrderService.Application.Realtime;

namespace MSA.OrderService.Infrastructure.Grpc;

internal sealed class InventoryAvailabilityGateway(
    InventoryAvailability.InventoryAvailabilityClient client,
    ILogger<InventoryAvailabilityGateway> logger)
    : MsaGrpcGateway<InventoryAvailabilityGateway>(logger), IInventoryAvailabilityGateway
{
    public Task<AvailabilityCheck?> CheckAsync(
        IReadOnlyList<AvailabilityRequestLine> lines,
        string userId,
        CancellationToken cancellationToken) =>
        CallAsync<AvailabilityCheck>(nameof(CheckAsync), async () =>
        {
            if (lines.Count == 0)
                return new AvailabilityCheck(true, []);

            var request = new CheckAvailabilityRequest { UserId = userId ?? string.Empty };
            request.Items.AddRange(lines.Select(line => new AvailabilityRequestItem
            {
                Sku = line.Sku,
                Quantity = line.Quantity,
                OfferId = line.OfferId?.ToString() ?? string.Empty
            }));

            var response = await client.CheckAvailabilityAsync(request, cancellationToken: cancellationToken);
            return new AvailabilityCheck(
                response.AllAvailable,
                response.Results
                    .Select(result => new AvailabilityLine(
                        result.Sku,
                        result.Available,
                        result.Sufficient,
                        string.IsNullOrWhiteSpace(result.Reason) ? null : result.Reason))
                    .ToArray());
        });
}

internal sealed class PaymentStatusGateway(
    PaymentQuery.PaymentQueryClient client,
    ILogger<PaymentStatusGateway> logger)
    : MsaGrpcGateway<PaymentStatusGateway>(logger), IPaymentStatusGateway
{
    public Task<PaymentStatusSnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
        CallAsync<PaymentStatusSnapshot>(nameof(GetAsync), async () =>
        {
            var response = await client.GetOrderPaymentAsync(
                new GetOrderPaymentRequest { OrderId = orderId.ToString() },
                cancellationToken: cancellationToken);
            if (!response.Found)
                return null;

            return new PaymentStatusSnapshot(
                Guid.Parse(response.PaymentId),
                response.Status,
                ParseAmount(response.Amount),
                response.Currency,
                response.Channel,
                string.IsNullOrWhiteSpace(response.PaymentUrl) ? null : response.PaymentUrl,
                ParseInstant(response.ExpiresOnUtc));
        });
}

internal sealed class CouponAuthorityGateway(
    CouponQuery.CouponQueryClient client,
    ILogger<CouponAuthorityGateway> logger)
    : MsaGrpcGateway<CouponAuthorityGateway>(logger), ICouponAuthority
{
    public Task<CouponConfirmation?> ConfirmAsync(
        IReadOnlyCollection<Guid> couponIds,
        string userId,
        CancellationToken cancellationToken) =>
        CallAsync<CouponConfirmation>(nameof(ConfirmAsync), async () =>
        {
            if (couponIds.Count == 0)
                return new CouponConfirmation(new Dictionary<Guid, ConfirmedCouponState>());

            var request = new ConfirmCouponsRequest { UserId = userId ?? string.Empty };
            request.CouponIds.AddRange(couponIds.Select(id => id.ToString()));
            var response = await client.ConfirmCouponsAsync(request, cancellationToken: cancellationToken);
            var items = response.Items
                .Where(item => Guid.TryParse(item.CouponId, out _))
                .ToDictionary(
                    item => Guid.Parse(item.CouponId),
                    item => new ConfirmedCouponState(
                        Guid.Parse(item.CouponId),
                        item.Usable,
                        string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason));
            return new CouponConfirmation(items);
        });
}

internal sealed class UserAddressAuthorityGateway(
    UserAddressQuery.UserAddressQueryClient client,
    ILogger<UserAddressAuthorityGateway> logger)
    : MsaGrpcGateway<UserAddressAuthorityGateway>(logger), IUserAddressAuthority
{
    public Task<AddressLookup?> GetAsync(string userId, Guid addressId, CancellationToken cancellationToken) =>
        CallAsync<AddressLookup>(nameof(GetAsync), async () =>
        {
            var response = await client.GetAddressAsync(
                new GetAddressRequest { UserId = userId, AddressId = addressId.ToString() },
                cancellationToken: cancellationToken);
            return response.Found
                ? new AddressLookup(true, new AuthoritativeAddress(
                    response.RecipientName,
                    response.PhoneNumber,
                    response.CountryCode,
                    response.Province,
                    response.City,
                    response.District,
                    response.Street,
                    string.IsNullOrWhiteSpace(response.PostalCode) ? null : response.PostalCode))
                : new AddressLookup(false, null);
        });
}
