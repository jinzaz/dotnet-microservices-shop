using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.ManageFulfillment;
using MSA.OrderService.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[Authorize(Policy = "order.admin.write")]
[ApiController]
[Route("order/admin/orders/{orderId:guid}/fulfillment")]
public sealed class AdminOrderFulfillmentController(ISender sender) : ControllerBase
{
    public sealed record ShipOrderRequest(
        [property: Required, MaxLength(50)] string CarrierCode,
        [property: Required, MaxLength(120)] string CarrierName,
        [property: Required, MaxLength(120)] string TrackingNumber,
        [property: MaxLength(1000)] string? Note);

    public sealed record FulfillmentNoteRequest(
        [property: MaxLength(1000)] string? Note);

    [HttpPost("ship")]
    public Task<OrderFulfillmentCommandResult> Ship(
        Guid orderId,
        ShipOrderRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        Send(
            orderId,
            idempotencyKey,
            OrderFulfillmentStatus.Shipped,
            request.CarrierCode,
            request.CarrierName,
            request.TrackingNumber,
            request.Note,
            cancellationToken);

    [HttpPost("in-transit")]
    public Task<OrderFulfillmentCommandResult> MarkInTransit(
        Guid orderId,
        FulfillmentNoteRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        Send(
            orderId,
            idempotencyKey,
            OrderFulfillmentStatus.InTransit,
            null,
            null,
            null,
            request.Note,
            cancellationToken);

    [HttpPost("delivered")]
    public Task<OrderFulfillmentCommandResult> MarkDelivered(
        Guid orderId,
        FulfillmentNoteRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken) =>
        Send(
            orderId,
            idempotencyKey,
            OrderFulfillmentStatus.Delivered,
            null,
            null,
            null,
            request.Note,
            cancellationToken);

    private Task<OrderFulfillmentCommandResult> Send(
        Guid orderId,
        string idempotencyKey,
        OrderFulfillmentStatus status,
        string? carrierCode,
        string? carrierName,
        string? trackingNumber,
        string? note,
        CancellationToken cancellationToken)
    {
        var actor = User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? throw new UnauthorizedAccessException();
        return sender.Send(
            new UpdateOrderFulfillmentCommand(
                orderId,
                idempotencyKey,
                status,
                carrierCode,
                carrierName,
                trackingNumber,
                note,
                actor),
            cancellationToken);
    }
}
