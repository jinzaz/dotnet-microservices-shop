using MediatR;
using MSA.OrderService.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSA.OrderService.Application.ManageFulfillment;

internal sealed class UpdateOrderFulfillmentCommandHandler(
    IOrderRepository orders,
    IOrderFulfillmentLock fulfillmentLock,
    TimeProvider clock)
    : IRequestHandler<UpdateOrderFulfillmentCommand, OrderFulfillmentCommandResult>
{
    public async Task<OrderFulfillmentCommandResult> Handle(
        UpdateOrderFulfillmentCommand request,
        CancellationToken cancellationToken)
    {
        await fulfillmentLock.AcquireAsync(request.OrderId, cancellationToken);
        var order = await orders.GetWithFulfillmentHistoryAsync(request.OrderId, cancellationToken)
            ?? throw new KeyNotFoundException("Order was not found.");

        var fingerprint = FulfillmentRequestFingerprint.Create(request);
        var now = clock.GetUtcNow();
        var changed = request.TargetStatus switch
        {
            OrderFulfillmentStatus.Shipped => order.Ship(
                request.IdempotencyKey,
                fingerprint,
                request.CarrierCode!,
                request.CarrierName!,
                request.TrackingNumber!,
                request.Note,
                request.Actor,
                now),
            OrderFulfillmentStatus.InTransit => order.MarkInTransit(
                request.IdempotencyKey,
                fingerprint,
                request.Note,
                request.Actor,
                now),
            OrderFulfillmentStatus.Delivered => order.MarkDelivered(
                request.IdempotencyKey,
                fingerprint,
                request.Note,
                request.Actor,
                now),
            _ => throw new InvalidOperationException("Unsupported fulfillment transition.")
        };

        return new OrderFulfillmentCommandResult(
            order.Id,
            order.FulfillmentStatus,
            changed,
            order.CarrierCode,
            order.CarrierName,
            order.TrackingNumber,
            order.UpdatedOnUtc);
    }
}

internal static class FulfillmentRequestFingerprint
{
    public static string Create(UpdateOrderFulfillmentCommand command)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            TargetStatus = command.TargetStatus.ToString(),
            CarrierCode = Normalize(command.CarrierCode, uppercase: true),
            CarrierName = Normalize(command.CarrierName, uppercase: false),
            TrackingNumber = Normalize(command.TrackingNumber, uppercase: true),
            Note = Normalize(command.Note, uppercase: false)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? Normalize(string? value, bool uppercase)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return uppercase ? normalized.ToUpperInvariant() : normalized;
    }
}
