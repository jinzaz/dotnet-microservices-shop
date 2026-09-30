using System.Globalization;
using Grpc.Core;
using MediatR;
using MSA.Grpc.Contracts.Inventory;
using MSA.InventoryService.Application;

namespace MSA.InventoryService.Grpc;

/// <summary>传输层适配：把契约翻译成应用层查询，可售规则只在 Application 里有一份。</summary>
internal sealed class InventoryAvailabilityGrpcService(ISender sender) : InventoryAvailability.InventoryAvailabilityBase
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
}
