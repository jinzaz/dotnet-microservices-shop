using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application.FlashSales;

namespace MSA.InventoryService.Endpoints;

internal static class FlashSaleEndpoints
{
    public static IEndpointRouteBuilder MapFlashSaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inventory/flash-sales/{campaignId:guid}", GetAsync)
            .RequireAuthorization("inventory-api-or-admin-read")
            .WithName("GetFlashSale")
            .WithSummary("Get a flash-sale campaign and its durable stock counters")
            .Produces<FlashSaleAvailabilityDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Results<Ok<FlashSaleAvailabilityDto>, NotFound>> GetAsync(
        Guid campaignId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetFlashSaleQuery(campaignId), cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }
}
