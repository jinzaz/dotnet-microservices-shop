using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Grpc.Core;
using MSA.Grpc.Contracts.Inventory;

namespace MSA.BFF.Web;

internal sealed record FlashSaleOfferView(
    Guid OfferId,
    string State,
    int TotalQuantity,
    int AvailableQuantity,
    int PerUserLimit,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc);

internal sealed record FlashSaleSnapshotView(string Sku, IReadOnlyList<FlashSaleOfferView> Offers, DateTimeOffset AsOfUtc);

/// <summary>
/// 把库存服务的 gRPC 服务端流转成浏览器能直接用的 SSE。浏览器端用原生 EventSource，
/// 断线（库存服务重启、网络抖动）后会按 retry 间隔自动重连，重连后先收到一份完整快照，不会丢状态。
/// 秒杀余量是公开信息，不需要登录。
/// </summary>
internal static class FlashSaleStreamEndpoints
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    internal static IEndpointRouteBuilder MapFlashSaleStreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bff/flash-sales/{sku}/stream", (
                string sku,
                InventoryAvailability.InventoryAvailabilityClient inventory,
                CancellationToken cancellationToken) =>
                TypedResults.ServerSentEvents(RelayAsync(sku, inventory, cancellationToken)))
            .AllowAnonymous()
            .WithTags("Flash Sale")
            .WithName("StreamFlashSaleOffers")
            .WithSummary("Server-sent events with the live remaining quantity of a SKU's flash-sale offers.");
        return endpoints;
    }

    private static async IAsyncEnumerable<SseItem<FlashSaleSnapshotView>> RelayAsync(
        string sku,
        InventoryAvailability.InventoryAvailabilityClient inventory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ponytail: 每个浏览器连接各开一条上游流（HTTP/2 多路复用，开销很小）；
        // 同一 SKU 的在线人数上万时，再改成 BFF 内按 SKU 共享一条上游流、本地扇出。
        using var call = inventory.WatchSkuOffers(new WatchSkuOffersRequest { Sku = sku }, cancellationToken: cancellationToken);
        var stream = call.ResponseStream.ReadAllAsync(cancellationToken);
        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            SkuOffersSnapshot snapshot;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    yield break;
                snapshot = enumerator.Current;
            }
            catch (RpcException exception) when (exception.StatusCode is StatusCode.Cancelled)
            {
                yield break;
            }

            yield return new SseItem<FlashSaleSnapshotView>(ToView(snapshot), "offers")
            {
                ReconnectionInterval = ReconnectDelay
            };
        }
    }

    private static FlashSaleSnapshotView ToView(SkuOffersSnapshot snapshot) => new(
        snapshot.Sku,
        snapshot.Offers
            .Select(offer => new FlashSaleOfferView(
                Guid.Parse(offer.OfferId),
                offer.State,
                offer.TotalQuantity,
                offer.AvailableQuantity,
                offer.PerUserLimit,
                Instant(offer.StartsOnUtc),
                Instant(offer.EndsOnUtc)))
            .ToArray(),
        Instant(snapshot.AsOfUtc));

    private static DateTimeOffset Instant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
