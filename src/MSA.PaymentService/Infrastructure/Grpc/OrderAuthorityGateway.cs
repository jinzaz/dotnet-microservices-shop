using MSA.Grpc;
using MSA.Grpc.Contracts.Order;
using MSA.PaymentService.Application.Realtime;

namespace MSA.PaymentService.Infrastructure.Grpc;

internal sealed class OrderAuthorityGateway(
    OrderQuery.OrderQueryClient client,
    ILogger<OrderAuthorityGateway> logger)
    : MsaGrpcGateway<OrderAuthorityGateway>(logger), IOrderAuthority
{
    public Task<OrderAuthoritySnapshot?> GetAsync(Guid orderId, CancellationToken cancellationToken) =>
        CallAsync<OrderAuthoritySnapshot>(nameof(GetAsync), async () =>
        {
            var response = await client.GetOrderSnapshotAsync(
                new GetOrderSnapshotRequest { OrderId = orderId.ToString() },
                cancellationToken: cancellationToken);
            if (!response.Found)
                return new OrderAuthoritySnapshot(false, orderId, string.Empty, 0m, string.Empty, string.Empty, null, null);

            return new OrderAuthoritySnapshot(
                true,
                orderId,
                response.UserId,
                ParseAmount(response.Amount),
                response.Currency,
                response.Status,
                Guid.TryParse(response.PricingSnapshotId, out var snapshotId) ? snapshotId : null,
                string.IsNullOrWhiteSpace(response.PricingSnapshotHash) ? null : response.PricingSnapshotHash);
        });
}
