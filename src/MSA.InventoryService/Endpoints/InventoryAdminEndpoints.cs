using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSA.InventoryService.Endpoints;

internal static class InventoryAdminEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/inventory/admin")
            .RequireAuthorization("inventory.admin.read");
        var write = endpoints.MapGroup("/inventory/admin")
            .RequireAuthorization("inventory.admin.write");

        write.MapPost("/stock", AddStockAsync)
            .WithName("AddInventoryStock")
            .WithSummary("Add on-hand inventory for a SKU")
            .Produces<StockMutationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        write.MapPost("/stock/adjustments", AdjustStockAsync)
            .WithName("AdjustInventoryStock")
            .WithSummary("Apply an idempotent audited stock adjustment")
            .Produces<AdjustStockResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        read.MapGet("/stock", ListStockAsync)
            .WithName("ListInventoryStock")
            .WithSummary("List durable stock balances with optional low-stock filtering")
            .Produces<InventoryStockPage>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        read.MapGet("/stock/{sku}/movements", GetStockMovementsAsync)
            .WithName("GetInventoryStockMovements")
            .WithSummary("Get recent audited stock movements for a SKU")
            .Produces<IReadOnlyList<StockMovementDto>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<Created<StockMutationResponse>> AddStockAsync(
        AddStockRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var id = await sender.Send(new AddStockCommand(request.Sku, request.Quantity), cancellationToken);
        return TypedResults.Created(
            $"/api/inventory/{Uri.EscapeDataString(request.Sku)}",
            new StockMutationResponse(id, request.Sku, request.Quantity));
    }

    private static async Task<Ok<AdjustStockResult>> AdjustStockAsync(
        AdjustStockRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        var operatorId = httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.FindFirstValue("client_id")
            ?? httpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException("The access token does not identify an inventory operator.");
        var result = await sender.Send(
            new AdjustStockCommand(
                request.Sku,
                request.QuantityDelta,
                request.Reason,
                idempotencyKey,
                operatorId),
            cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<InventoryStockPage>, ValidationProblem>> ListStockAsync(
        ISender sender,
        CancellationToken cancellationToken,
        string? search = null,
        int? lowStockThreshold = null,
        int skip = 0,
        int take = 50)
    {
        if (lowStockThreshold is < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(lowStockThreshold)] = ["Low-stock threshold cannot be negative."]
            });
        }

        return TypedResults.Ok(await sender.Send(
            new ListInventoryStockQuery(search, lowStockThreshold, skip, take),
            cancellationToken));
    }

    private static async Task<Ok<IReadOnlyList<StockMovementDto>>> GetStockMovementsAsync(
        string sku,
        int? take,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetStockMovementsQuery(sku, take ?? 100),
            cancellationToken);
        return TypedResults.Ok(result);
    }
}

/// <summary>Payload for adding on-hand stock.</summary>
public sealed record AddStockRequest
{
    [Required, MaxLength(80)]
    public required string Sku { get; init; }

    [Range(1, int.MaxValue)]
    public required int Quantity { get; init; }
}

/// <summary>Result of an inventory stock mutation.</summary>
public sealed record StockMutationResponse(Guid Id, string Sku, int AddedQuantity);

/// <summary>Audited positive or negative adjustment to an on-hand stock balance.</summary>
public sealed record AdjustStockRequest
{
    [Required, MaxLength(80)]
    public required string Sku { get; init; }

    public required int QuantityDelta { get; init; }

    [Required, MaxLength(500)]
    public required string Reason { get; init; }
}
