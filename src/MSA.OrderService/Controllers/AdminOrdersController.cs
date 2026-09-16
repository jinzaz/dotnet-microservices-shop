using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Domain;
using System.ComponentModel.DataAnnotations;

namespace MSA.OrderService.Controllers;

[ApiController]
[Authorize(Policy = "order.admin.read")]
[Route("order/admin/orders")]
public sealed class AdminOrdersController(ISender sender) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<OrderListItemDto>> List(
        [FromQuery] string? userId,
        [FromQuery] OrderFulfillmentStatus? fulfillmentStatus,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetOrdersQuery(
            string.Empty,
            true,
            userId,
            fulfillmentStatus,
            skip,
            take), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var value = await sender.Send(new GetOrderQuery(id, string.Empty, true), cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
