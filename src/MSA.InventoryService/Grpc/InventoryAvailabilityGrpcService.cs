using System.Globalization;
using System.Threading.Channels;
using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Options;
using MSA.Grpc.Contracts.Inventory;
using MSA.InventoryService.Application;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Domain;
using MSA.Redis;

namespace MSA.InventoryService.Grpc;

/// <summary>传输层适配：把契约翻译成应用层查询，可售规则只在 Application 里有一份。</summary>
internal sealed class InventoryAvailabilityGrpcService(
    ISender sender,
    IChangeFeed changes,
    IOptions<OfferStreamOptions> streamOptions,
    ILogger<InventoryAvailabilityGrpcService> logger) : InventoryAvailability.InventoryAvailabilityBase
{
    public override async Task<CheckAvailabilityResponse> CheckAvailability(
        CheckAvailabilityRequest request,
        ServerCallContext context)
    {
        var lines = request.Items
            .Select(item => new AvailabilityRequestLine(
                item.Sku,
                item.Quantity,
                Guid.TryParse(item.OfferId, out var offerId) ? offerId : null))
            .ToArray();

        var snapshot = await sender.Send(
            new CheckAvailabilityQuery(lines, string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId),
            context.CancellationToken);

        var response = new CheckAvailabilityResponse { AllAvailable = snapshot.AllAvailable };
        response.Results.AddRange(snapshot.Lines.Select(line => new AvailabilityResult
        {
            Sku = line.Sku,
            Available = line.Available,
            Sufficient = line.Sufficient,
            Reason = line.Reason ?? string.Empty
        }));
        return response;
    }

    public override async Task<GetOrderReservationResponse> GetOrderReservation(
        GetOrderReservationRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid order id is required."));

        var reservation = await sender.Send(new GetOrderReservationQuery(orderId), context.CancellationToken);
        if (reservation is null)
            return new GetOrderReservationResponse { Found = false };

        var response = new GetOrderReservationResponse
        {
            Found = true,
            ReservationId = reservation.ReservationId.ToString(),
            Status = reservation.Status,
            CreatedOnUtc = reservation.CreatedOnUtc.ToString("O", CultureInfo.InvariantCulture),
            ExpiresOnUtc = reservation.ExpiresOnUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        response.Lines.AddRange(reservation.Lines.Select(line => new ReservationLine
        {
            Sku = line.Sku,
            Quantity = line.Quantity,
            OfferId = line.OfferId?.ToString() ?? string.Empty
        }));
        return response;
    }

    /// <summary>
    /// 秒杀余量推流。变更靠提交后发出的通知唤醒（至多一次投递），同时每隔一段时间主动刷新一次兜底，
    /// 也顺带推出「未开始 → 进行中 → 已结束」这类只随时间变化的状态。
    /// 一个窗口内的连续变化合并成一次推送：开抢时每秒上千次扣减，每个订阅者每秒最多收到几条。
    /// 快照内容没变就不推；读的是读模型缓存，订阅者再多，数据库也只在每次失效后被读一次。
    /// </summary>
    public override async Task WatchSkuOffers(
        WatchSkuOffersRequest request,
        IServerStreamWriter<SkuOffersSnapshot> responseStream,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A SKU is required."));

        var sku = StockItem.NormalizeSku(request.Sku);
        var options = streamOptions.Value;
        var cancellationToken = context.CancellationToken;
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

        IAsyncDisposable? subscription = null;
        try
        {
            subscription = await changes.SubscribeAsync(
                InventoryCacheKeys.OffersChangedTopic(sku),
                _ => signals.Writer.TryWrite(true),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 订阅不上就退化成定时刷新，推流照样工作，只是慢一点。
            logger.LogWarning(exception, "Offer change feed unavailable for {Sku}; falling back to polling.", sku);
        }

        try
        {
            string? lastSent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var offers = await sender.Send(new GetSkuOffersQuery(sku), cancellationToken);
                var snapshot = ToSnapshot(sku, offers);
                var fingerprint = string.Join('|', snapshot.Offers.Select(offer => offer.ToString()));
                if (fingerprint != lastSent)
                {
                    await responseStream.WriteAsync(snapshot, cancellationToken);
                    lastSent = fingerprint;
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(options.RefreshInterval);
                try
                {
                    await signals.Reader.ReadAsync(wait.Token);
                    await Task.Delay(options.CoalesceWindow, cancellationToken);
                    signals.Reader.TryRead(out _);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 到了刷新周期，没有新通知也再看一眼。
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 客户端断开。
        }
        finally
        {
            if (subscription is not null)
                await subscription.DisposeAsync();
        }
    }

    private static SkuOffersSnapshot ToSnapshot(string sku, IReadOnlyList<InventoryAllocationDto> offers)
    {
        var snapshot = new SkuOffersSnapshot
        {
            Sku = sku,
            AsOfUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        snapshot.Offers.AddRange(offers.Select(offer => new SkuOffer
        {
            AllocationId = offer.AllocationId.ToString(),
            OfferId = offer.OfferId.ToString(),
            State = offer.State,
            TotalQuantity = offer.TotalQuantity,
            AvailableQuantity = offer.AvailableQuantity,
            PerUserLimit = offer.PerUserLimit,
            StartsOnUtc = offer.StartsOnUtc.ToString("O", CultureInfo.InvariantCulture),
            EndsOnUtc = offer.EndsOnUtc.ToString("O", CultureInfo.InvariantCulture)
        }));
        return snapshot;
    }
}

/// <summary>秒杀余量推流的节奏：合并窗口决定每个订阅者每秒最多几条，刷新周期是通知丢失时的兜底。</summary>
public sealed class OfferStreamOptions
{
    public const string SectionName = "OfferStream";
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(5);
}
