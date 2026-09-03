using MediatR;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
namespace MSA.OrderService.Controllers;
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    public sealed record CreateOrderRequest(string ProductId, int Quantity, decimal Amount, string Currency = "CNY");
    [HttpPost]
    public async Task<ActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateOrderCommand(request.ProductId, request.Quantity, request.Amount, request.Currency), cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { orderId = id });
    }
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var order = await sender.Send(new GetOrderQuery(id), cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }
}
