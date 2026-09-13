using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application;

namespace MSA.InventoryService.Endpoints;

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inventory/{sku}", GetAvailabilityAsync)
            .RequireAuthorization("inventory-api")
            .WithName("GetInventoryAvailability")
            .WithSummary("Get durable inventory availability for a SKU")
            .Produces<InventoryAvailabilityDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

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
}
