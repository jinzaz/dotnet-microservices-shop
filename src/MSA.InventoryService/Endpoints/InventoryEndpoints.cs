using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application;
using System.ComponentModel.DataAnnotations;

namespace MSA.InventoryService.Endpoints;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/inventory").RequireAuthorization();

        group.MapGet("/{sku}", GetAvailabilityAsync)
            .WithName("GetInventoryAvailability")
            .WithSummary("Get durable inventory availability for a SKU")
            .Produces<InventoryAvailabilityDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/stock", AddStockAsync)
            .WithName("AddInventoryStock")
            .WithSummary("Add on-hand inventory for a SKU")
            .Produces<StockMutationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization("inventory.write");

        return endpoints;
    }

    private static async Task<Results<Ok<InventoryAvailabilityDto>, NotFound>> GetAvailabilityAsync(
        string sku,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInventoryAvailabilityQuery(sku), cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
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
