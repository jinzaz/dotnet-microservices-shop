using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MSA.OrderService.Application.CreateFlashSaleOrder;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[Authorize]
[EnableRateLimiting("FlashSaleSubmission")]
[ApiController]
[Route("api/flash-sales/{campaignId:guid}/orders")]
public sealed class FlashSaleOrdersController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<FlashSaleOrderAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<FlashSaleOrderAcceptedResponse>> Create(
        Guid campaignId,
        CreateFlashSaleOrderRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();
        var orderId = await sender.Send(
            new CreateFlashSaleOrderCommand(
                userId,
                idempotencyKey,
                campaignId,
                request.Quantity,
                request.PaymentMethod),
            cancellationToken);
        return AcceptedAtAction(
            nameof(OrdersController.Get),
            "Orders",
            new { id = orderId },
            new FlashSaleOrderAcceptedResponse(orderId));
    }
}

/// <summary>Payload for submitting one flash-sale order.</summary>
public sealed record CreateFlashSaleOrderRequest
{
    [Range(1, 999)]
    public int Quantity { get; init; } = 1;

    [Required]
    public required string PaymentMethod { get; init; }
}

/// <summary>Order identifier accepted for asynchronous inventory reservation.</summary>
public sealed record FlashSaleOrderAcceptedResponse(Guid OrderId);
