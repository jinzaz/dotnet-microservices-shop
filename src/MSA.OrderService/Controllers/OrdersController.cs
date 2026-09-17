using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MSA.OrderService.Application.CancelOrder;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[ApiController]
[Route("order/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    public sealed record CreateOrderItemRequest(string Sku, int Quantity, Guid? OfferId = null);

    public sealed record CreateOrderRequest(
        IReadOnlyList<CreateOrderItemRequest> Items,
        string PaymentMethod,
        ShippingAddressInput ShippingAddress,
        IReadOnlyList<string>? CouponCodes = null,
        Guid? SourceBasketId = null,
        long? SourceBasketVersion = null);

    public sealed record CancelOrderRequest([property: MaxLength(200)] string? Reason = null);

    [HttpPost]
    [Authorize(Policy = "order-api")]
    [EnableRateLimiting("OrderSubmission")]
    public async Task<ActionResult> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        var id = await sender.Send(
            new CreateOrderCommand(
                userId,
                idempotencyKey,
                request.Items.Select(item => new CreateOrderItem(item.Sku, item.Quantity, item.OfferId)).ToArray(),
                request.PaymentMethod,
                request.ShippingAddress,
                request.CouponCodes,
                request.SourceBasketId,
                request.SourceBasketVersion),
            cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { orderId = id });
    }

    [HttpGet]
    [Authorize(Policy = "order-api")]
    public Task<IReadOnlyList<OrderListItemDto>> List(
        [FromQuery] OrderFulfillmentStatus? fulfillmentStatus,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var requestingUserId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        return sender.Send(
            new GetOrdersQuery(
                requestingUserId,
                false,
                null,
                fulfillmentStatus,
                skip,
                take),
            cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "order-api")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        var value = await sender.Send(
            new GetOrderQuery(
                id,
                userId,
                false),
            cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }

    /// <summary>Cancels an order that has not been paid yet; repeated calls are idempotent.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "order-api")]
    public Task<CancelOrderResult> Cancel(
        Guid id,
        CancelOrderRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        return sender.Send(new CancelOrderCommand(id, userId, request?.Reason), cancellationToken);
    }
}
