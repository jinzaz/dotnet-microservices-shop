using Microsoft.AspNetCore.Http.HttpResults;
using MSA.InventoryService.Application.Allocations;
using MSA.InventoryService.Domain;
using System.ComponentModel.DataAnnotations;

namespace MSA.InventoryService.Endpoints;

internal static class InventoryAllocationEndpoints
{
    public static IEndpointRouteBuilder MapInventoryAllocationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inventory/allocations/{allocationId:guid}", GetAsync)
            .RequireAuthorization("inventory-api-or-admin-read")
            .WithName("GetInventoryAllocation")
            .Produces<InventoryAllocationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var read = endpoints.MapGroup("/inventory/admin/allocations")
            .RequireAuthorization("inventory.admin.read");
        var write = endpoints.MapGroup("/inventory/admin/allocations")
            .RequireAuthorization("inventory.admin.write");

        read.MapGet("/", ListAsync)
            .WithName("ListInventoryAllocations")
            .Produces<InventoryAllocationPage>(StatusCodes.Status200OK);
        write.MapPost("/", CreateAsync)
            .WithName("CreateInventoryAllocation")
            .Produces<InventoryAllocationCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        write.MapPost("/{allocationId:guid}/close", CloseAsync)
            .WithName("CloseInventoryAllocation")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // Compatibility surface: old flash-sale URLs now delegate to the same allocation commands.
    public static IEndpointRouteBuilder MapFlashSaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/inventory/flash-sales/{campaignId:guid}", async (
            Guid campaignId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var value = await sender.Send(new GetInventoryAllocationQuery(campaignId), cancellationToken);
            return value is null
                ? Results.NotFound()
                : Results.Ok(ToCompatibilityDto(value));
        })
        .RequireAuthorization("inventory-api-or-admin-read")
        .WithName("GetFlashSale");
        return endpoints;
    }

    public static IEndpointRouteBuilder MapFlashSaleAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/inventory/admin/flash-sales")
            .RequireAuthorization("inventory.admin.read");
        var write = endpoints.MapGroup("/inventory/admin/flash-sales")
            .RequireAuthorization("inventory.admin.write");

        read.MapGet("/", async (
            ISender sender,
            CancellationToken cancellationToken,
            string? sku = null,
            InventoryAllocationStatus? status = null,
            int skip = 0,
            int take = 50) =>
        {
            var page = await sender.Send(new ListInventoryAllocationsQuery(sku, status, skip, take), cancellationToken);
            return Results.Ok(new FlashSaleCompatibilityPage(
                page.Items.Select(ToCompatibilityDto).ToArray(),
                page.Skip,
                page.Take,
                page.Total));
        }).WithName("ListFlashSales");

        write.MapPost("/", async (
            CreateFlashSaleCompatibilityRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var offerId = request.OfferId ?? Guid.NewGuid();
            var id = await sender.Send(new CreateInventoryAllocationCommand(
                offerId,
                request.Sku,
                request.TotalQuantity,
                request.StartsOnUtc,
                request.EndsOnUtc,
                request.PurchaseLimit), cancellationToken);
            return Results.Created(
                $"/api/inventory/flash-sales/{id}",
                new FlashSaleCreatedCompatibilityResponse(id, offerId));
        }).WithName("CreateFlashSale");

        write.MapPost("/{campaignId:guid}/close", async (
            Guid campaignId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new CloseInventoryAllocationCommand(campaignId), cancellationToken);
            return result.Found ? Results.NoContent() : Results.NotFound();
        }).WithName("CloseFlashSale");
        return endpoints;
    }

    private static async Task<Results<Ok<InventoryAllocationDto>, NotFound>> GetAsync(
        Guid allocationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInventoryAllocationQuery(allocationId), cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Ok<InventoryAllocationPage>> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        string? sku = null,
        InventoryAllocationStatus? status = null,
        int skip = 0,
        int take = 50) =>
        TypedResults.Ok(await sender.Send(
            new ListInventoryAllocationsQuery(sku, status, skip, take),
            cancellationToken));

    private static async Task<Created<InventoryAllocationCreatedResponse>> CreateAsync(
        CreateInventoryAllocationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var id = await sender.Send(new CreateInventoryAllocationCommand(
            request.OfferId,
            request.Sku,
            request.TotalQuantity,
            request.StartsOnUtc,
            request.EndsOnUtc,
            request.PerUserLimit), cancellationToken);
        return TypedResults.Created(
            $"/api/inventory/allocations/{id}",
            new InventoryAllocationCreatedResponse(id));
    }

    private static async Task<Results<NoContent, NotFound>> CloseAsync(
        Guid allocationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CloseInventoryAllocationCommand(allocationId), cancellationToken);
        return result.Found ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static FlashSaleCompatibilityDto ToCompatibilityDto(InventoryAllocationDto value) =>
        new(
            value.AllocationId,
            value.OfferId,
            value.Sku,
            "SingleRow",
            value.State,
            value.TotalQuantity,
            value.ReservedQuantity,
            value.SoldQuantity,
            value.ReleasedQuantity,
            value.AvailableQuantity,
            value.PerUserLimit,
            value.StartsOnUtc,
            value.EndsOnUtc);
}

public sealed record CreateInventoryAllocationRequest
{
    public required Guid OfferId { get; init; }
    [Required, MaxLength(80)] public required string Sku { get; init; }
    [Range(1, 10_000_000)] public required int TotalQuantity { get; init; }
    public required DateTimeOffset StartsOnUtc { get; init; }
    public required DateTimeOffset EndsOnUtc { get; init; }
    [Range(1, 999)] public int PerUserLimit { get; init; } = 1;
}

public sealed record InventoryAllocationCreatedResponse(Guid AllocationId);

public sealed record CreateFlashSaleCompatibilityRequest
{
    public Guid? OfferId { get; init; }
    [Required, MaxLength(80)] public required string Sku { get; init; }
    [Range(1, 10_000_000)] public required int TotalQuantity { get; init; }
    public required DateTimeOffset StartsOnUtc { get; init; }
    public required DateTimeOffset EndsOnUtc { get; init; }
    [Range(1, 999)] public int PurchaseLimit { get; init; } = 1;
}

public sealed record FlashSaleCreatedCompatibilityResponse(Guid CampaignId, Guid OfferId);

public sealed record FlashSaleCompatibilityDto(
    Guid CampaignId,
    Guid OfferId,
    string Sku,
    string Strategy,
    string State,
    int TotalQuantity,
    int ReservedQuantity,
    int SoldQuantity,
    int ReleasedQuantity,
    int AvailableQuantity,
    int PurchaseLimit,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc);

public sealed record FlashSaleCompatibilityPage(
    IReadOnlyList<FlashSaleCompatibilityDto> Items,
    int Skip,
    int Take,
    int Total);
