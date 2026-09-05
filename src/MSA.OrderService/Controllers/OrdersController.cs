using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[Authorize]
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    public sealed record CreateOrderItemRequest(string Sku, int Quantity);

    public sealed record CreateOrderRequest(
        IReadOnlyList<CreateOrderItemRequest> Items,
        string PaymentMethod,
        IReadOnlyList<string>? CouponCodes = null);

    [HttpPost]
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
                request.CouponCodes),
            cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { orderId = id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? string.Empty;
        var value = await sender.Send(
            new GetOrderQuery(
                id,
                userId,
                User.IsInRole("admin") || User.IsInRole("operator")),
            cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
