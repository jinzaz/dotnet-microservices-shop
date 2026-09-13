using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application.FlashSales;
using MSA.InventoryService.Domain;
using System.ComponentModel.DataAnnotations;

namespace MSA.InventoryService.Endpoints;

internal static class FlashSaleAdminEndpoints
{
    public static IEndpointRouteBuilder MapFlashSaleAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/inventory/admin/flash-sales")
            .RequireAuthorization("inventory.admin.write");

        admin.MapPost("/", CreateAsync)
            .WithName("CreateFlashSale")
            .WithSummary("Allocate inventory and create a flash-sale campaign")
            .Produces<FlashSaleCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPost("/{campaignId:guid}/close", CloseAsync)
            .WithName("CloseFlashSale")
            .WithSummary("Close a campaign and release inventory not held by orders")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Created<FlashSaleCreatedResponse>> CreateAsync(
        CreateFlashSaleRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var campaignId = await sender.Send(
            new CreateFlashSaleCommand(
                request.Sku,
                request.TotalQuantity,
                request.StartsOnUtc,
                request.EndsOnUtc,
                request.PurchaseLimit,
                request.Strategy,
                request.BucketCount),
            cancellationToken);
        return TypedResults.Created(
            $"/api/inventory/flash-sales/{campaignId}",
            new FlashSaleCreatedResponse(campaignId));
    }

    private static async Task<Results<NoContent, NotFound>> CloseAsync(
        Guid campaignId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CloseFlashSaleCommand(campaignId), cancellationToken);
        return result.Found ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}

/// <summary>Payload for allocating stock to a flash-sale campaign.</summary>
public sealed record CreateFlashSaleRequest
{
    [Required, MaxLength(80)]
    public required string Sku { get; init; }

    [Range(1, 10_000_000)]
    public required int TotalQuantity { get; init; }

    public required DateTimeOffset StartsOnUtc { get; init; }

    public required DateTimeOffset EndsOnUtc { get; init; }

    [Range(1, 999)]
    public int PurchaseLimit { get; init; } = 1;

    public FlashSaleStockStrategy? Strategy { get; init; }

    [Range(1, 128)]
    public int? BucketCount { get; init; }
}

/// <summary>Identifier returned after a flash-sale campaign is created.</summary>
public sealed record FlashSaleCreatedResponse(Guid CampaignId);
