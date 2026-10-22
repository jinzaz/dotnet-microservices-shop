using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.AfterSales;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[ApiController]
[Authorize(Policy = "order-api")]
[Route("order")]
public sealed class AfterSalesController(ISender sender) : ControllerBase
{
    public sealed record AfterSalesItemRequest(
        [Required, MaxLength(80)] string Sku,
        [Range(1, 999)] int Quantity);

    public sealed record CreateAfterSalesRequest(
        AfterSalesType Type,
        AfterSalesReason ReasonCode,
        [MaxLength(500)] string? Description,
        [Required, MinLength(1), MaxLength(50)] IReadOnlyList<AfterSalesItemRequest> Lines,
        [MaxLength(AfterSalesRequest.MaxEvidenceCount)] IReadOnlyList<string>? EvidenceObjectKeys);

    public sealed record ReturnShipmentRequest(
        [Required, MaxLength(120)] string CarrierName,
        [Required, MaxLength(120)] string TrackingNumber);

    public sealed record EvidenceUploadRequest(
        [Required, MaxLength(255)] string FileName,
        [Required, MaxLength(100)] string ContentType,
        long ContentLength);

    [HttpGet("after-sales")]
    public Task<IReadOnlyList<AfterSalesSummaryDto>> List(
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, 100)] int take = 20,
        CancellationToken cancellationToken = default) =>
        sender.Send(new ListAfterSalesQuery(UserId, null, null, null, null, skip, take), cancellationToken);

    [HttpGet("after-sales/{id:guid}")]
    public async Task<ActionResult<AfterSalesDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetAfterSalesQuery(id, UserId), cancellationToken) is { } value ? Ok(value) : NotFound();

    [HttpGet("orders/{orderId:guid}/after-sales")]
    public Task<IReadOnlyList<AfterSalesSummaryDto>> ListForOrder(Guid orderId, CancellationToken cancellationToken) =>
        sender.Send(new ListAfterSalesQuery(UserId, orderId, null, null, null, 0, 100), cancellationToken);

    [HttpPost("orders/{orderId:guid}/after-sales")]
    public async Task<ActionResult<AfterSalesDto>> Create(
        Guid orderId,
        CreateAfterSalesRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required, MaxLength(100)] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var id = await sender.Send(
            new CreateAfterSalesCommand(
                orderId,
                UserId,
                idempotencyKey,
                request.Type,
                request.ReasonCode,
                request.Description,
                request.Lines.Select(line => new AfterSalesItem(line.Sku, line.Quantity)).ToArray(),
                request.EvidenceObjectKeys ?? []),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, await Detail(id, cancellationToken));
    }

    /// <summary>Signs a direct upload of one evidence image to the private bucket.</summary>
    [HttpPost("after-sales/evidence/upload-url")]
    public EvidenceUploadTicket CreateEvidenceUploadUrl(
        EvidenceUploadRequest request,
        [FromServices] AfterSalesEvidenceStorage evidence) =>
        evidence.CreateUploadUrl(UserId, request.FileName, request.ContentType, request.ContentLength);

    [HttpPost("after-sales/{id:guid}/cancel")]
    public Task<AfterSalesDto> Cancel(Guid id, CancellationToken cancellationToken) =>
        Act(new AfterSalesActionCommand(id, UserId, true, AfterSalesAction.Cancel), cancellationToken);

    [HttpPost("after-sales/{id:guid}/return-shipment")]
    public Task<AfterSalesDto> SubmitReturnShipment(Guid id, ReturnShipmentRequest request, CancellationToken cancellationToken) =>
        Act(new AfterSalesActionCommand(id, UserId, true, AfterSalesAction.SubmitReturnShipment,
            CarrierName: request.CarrierName, TrackingNumber: request.TrackingNumber), cancellationToken);

    /// <summary>Confirms receipt of an exchanged or reshipped item, closing the request.</summary>
    [HttpPost("after-sales/{id:guid}/confirm-receipt")]
    public Task<AfterSalesDto> ConfirmReceipt(Guid id, CancellationToken cancellationToken) =>
        Act(new AfterSalesActionCommand(id, UserId, true, AfterSalesAction.ConfirmReceipt), cancellationToken);

    private string UserId => User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();

    private async Task<AfterSalesDto> Act(AfterSalesActionCommand command, CancellationToken cancellationToken) =>
        await Detail(await sender.Send(command, cancellationToken), cancellationToken);

    private async Task<AfterSalesDto> Detail(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetAfterSalesQuery(id, UserId), cancellationToken)
            ?? throw new KeyNotFoundException("After-sales request was not found.");
}
