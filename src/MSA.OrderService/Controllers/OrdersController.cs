using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Domain;
using MSA.Infrastrucute.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[ApiController]
[Route("order/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    public sealed record CreateOrderItemRequest(string Sku, int Quantity);

    public sealed record CreateOrderRequest(
        IReadOnlyList<CreateOrderItemRequest> Items,
        string PaymentMethod,
        ShippingAddressInput ShippingAddress,
        IReadOnlyList<string>? CouponCodes = null,
        Guid? SourceBasketId = null,
        long? SourceBasketVersion = null);

    [HttpPost]
    [Authorize(Policy = "order-api")]
    public async Task<ActionResult> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        var id = await sender.Send(
            new CreateOrderCommand(
                userId,
                idempotencyKey,
                request.Items.Select(item => new CreateOrderItem(item.Sku, item.Quantity)).ToArray(),
                request.PaymentMethod,
                request.ShippingAddress,
                request.CouponCodes,
                request.SourceBasketId,
                request.SourceBasketVersion),
            cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { orderId = id });
    }

    [HttpGet]
    [Authorize(Policy = "order-api-or-admin-read")]
    public Task<IReadOnlyList<OrderListItemDto>> List(
        [FromQuery] string? userId,
        [FromQuery] OrderFulfillmentStatus? fulfillmentStatus,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var requestingUserId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        var canReadAll = User.HasMsaScope("order.admin.read");
        return sender.Send(
            new GetOrdersQuery(
                requestingUserId,
                canReadAll,
                canReadAll ? userId : null,
                fulfillmentStatus,
                skip,
                take),
            cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "order-api-or-admin-read")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? string.Empty;
        var value = await sender.Send(
            new GetOrderQuery(
                id,
                userId,
                User.HasMsaScope("order.admin.read")),
            cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
