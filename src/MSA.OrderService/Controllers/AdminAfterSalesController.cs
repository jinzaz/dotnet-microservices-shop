using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSA.OrderService.Application.AfterSales;
using MSA.OrderService.Domain;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.OrderService.Controllers;

[ApiController]
[Route("order/admin/after-sales")]
public sealed class AdminAfterSalesController(ISender sender) : ControllerBase
{
    public sealed record ApproveRequest(
        [property: MaxLength(500)] string? ReturnAddress,
        [property: MaxLength(1000)] string? Note);

    public sealed record RejectRequest([property: Required, MaxLength(500)] string Reply);

    public sealed record NoteRequest([property: MaxLength(1000)] string? Note);

    public sealed record ShipReplacementRequest(
        [property: Required, MaxLength(50)] string CarrierCode,
        [property: Required, MaxLength(120)] string CarrierName,
        [property: Required, MaxLength(120)] string TrackingNumber,
        [property: MaxLength(1000)] string? Note);

    public sealed record RestockLineRequest(
        [property: Required, MaxLength(80)] string Sku,
        [property: Range(0, 999)] int Quantity);

    /// <summary>Units per SKU going back to sellable stock; a note is required when any unit is not restocked.</summary>
    public sealed record DisposeReturnRequest(
        [property: Required, MaxLength(50)] IReadOnlyList<RestockLineRequest> Lines,
        [property: MaxLength(500)] string? Note);

    [HttpGet]
    [Authorize(Policy = "order.admin.read")]
    public Task<IReadOnlyList<AfterSalesSummaryDto>> List(
        [FromQuery] AfterSalesStatus? status,
        [FromQuery] AfterSalesType? type,
        [FromQuery] Guid? orderId,
        [FromQuery, MaxLength(120)] string? search,
        [FromQuery] ReturnDisposition? disposition,
        [FromQuery] ReplacementStockStatus? replacementStock,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, 200)] int take = 50,
        CancellationToken cancellationToken = default) =>
        sender.Send(new ListAfterSalesQuery(null, orderId, status, type, search, skip, take, disposition, replacementStock), cancellationToken);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "order.admin.read")]
    public async Task<ActionResult<AfterSalesDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetAfterSalesQuery(id, null), cancellationToken) is { } value ? Ok(value) : NotFound();

    /// <summary>Refund-only starts the refund at once; return types need a return address; reship waits for shipment.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> Approve(Guid id, ApproveRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.Approve, cancellationToken, note: request.Note, returnAddress: request.ReturnAddress);

    /// <summary>Rejects at review or after inspecting returned goods; the reply is shown to the customer.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> Reject(Guid id, RejectRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.Reject, cancellationToken, note: request.Reply);

    [HttpPost("{id:guid}/receive-return")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> ReceiveReturn(Guid id, NoteRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.ConfirmReturnReceived, cancellationToken, note: request.Note);

    /// <summary>Refunds through the original payment; also retries a failed refund.</summary>
    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> Refund(Guid id, NoteRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.Refund, cancellationToken, note: request.Note);

    [HttpPost("{id:guid}/ship-replacement")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> ShipReplacement(Guid id, ShipReplacementRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.ShipReplacement, cancellationToken, request.Note, null, request.CarrierCode, request.CarrierName, request.TrackingNumber);

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> Complete(Guid id, NoteRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.Complete, cancellationToken, note: request.Note);

    /// <summary>Records the inspection outcome of returned goods: restocked units go back to sellable inventory.</summary>
    [HttpPost("{id:guid}/dispose-return")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> DisposeReturn(Guid id, DisposeReturnRequest request, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.DisposeReturn, cancellationToken, note: request.Note,
            restock: request.Lines.Select(line => new AfterSalesItem(line.Sku, line.Quantity)).ToArray());

    /// <summary>Asks Inventory again to hold the exchange or reship replacement after a shortage.</summary>
    [HttpPost("{id:guid}/reserve-stock")]
    [Authorize(Policy = "order.admin.write")]
    public Task<AfterSalesDto> ReserveStock(Guid id, CancellationToken cancellationToken) =>
        Act(id, AfterSalesAction.RetryReplacementStock, cancellationToken);

    private async Task<AfterSalesDto> Act(
        Guid id,
        AfterSalesAction action,
        CancellationToken cancellationToken,
        string? note = null,
        string? returnAddress = null,
        string? carrierCode = null,
        string? carrierName = null,
        string? trackingNumber = null,
        IReadOnlyList<AfterSalesItem>? restock = null)
    {
        var actor = User.FindFirstValue("sub") ?? User.Identity?.Name ?? throw new UnauthorizedAccessException();
        await sender.Send(
            new AfterSalesActionCommand(id, actor, false, action, note, returnAddress, carrierCode, carrierName, trackingNumber, restock),
            cancellationToken);
        return await sender.Send(new GetAfterSalesQuery(id, null), cancellationToken)
            ?? throw new KeyNotFoundException("After-sales request was not found.");
    }
}
