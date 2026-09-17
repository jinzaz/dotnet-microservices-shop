using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Application.GetOrdersSummary;
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
        [FromQuery, MaxLength(120)] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetOrdersQuery(
            string.Empty,
            true,
            userId,
            fulfillmentStatus,
            skip,
            take,
            search), cancellationToken);

    /// <summary>Order and fulfillment status counts plus a daily created/paid trend for the operations dashboard.</summary>
    [HttpGet("summary")]
    public Task<OrdersSummaryDto> Summary(
        [FromQuery, Range(1, 31)] int days = 7,
        [FromQuery, Range(-840, 840)] int utcOffsetMinutes = 0,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetOrdersSummaryQuery(days, utcOffsetMinutes), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var value = await sender.Send(new GetOrderQuery(id, string.Empty, true), cancellationToken);
        return value is null ? NotFound() : Ok(value);
    }
}
