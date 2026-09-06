using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using MSA.BasketService.Application;
using MSA.BasketService.Domain;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;

namespace MSA.BasketService.Endpoints;

internal static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/basket")
            .RequireAuthorization()
            .WithTags("Basket");

        group.MapGet("/", GetAsync)
            .WithName("GetMyBasket")
            .WithSummary("Get the authenticated user's basket and current local product snapshots")
            .Produces<BasketDto>();

        group.MapPost("/items", AddItemAsync)
            .WithName("AddBasketItem")
            .WithSummary("Add a sellable product or increase its quantity")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapPut("/items/{sku}", SetItemQuantityAsync)
            .WithName("SetBasketItemQuantity")
            .WithSummary("Set the quantity of an existing basket item")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapDelete("/items/{sku}", RemoveItemAsync)
            .WithName("RemoveBasketItem")
            .WithSummary("Idempotently remove an item from the basket")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapPut("/coupons", ReplaceCouponsAsync)
            .WithName("ReplaceBasketCoupons")
            .WithSummary("Replace optional coupon codes; Ordering remains the pricing authority")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapDelete("/", ClearAsync)
            .WithName("ClearBasket")
            .WithSummary("Idempotently clear all items and coupon codes")
            .Produces<BasketDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        return endpoints;
    }

    private static async Task<Ok<BasketDto>> GetAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        var basket = await sender.Send(new GetBasketQuery(userId), cancellationToken);
        SetETag(httpContext.Response, basket.Version);
        return TypedResults.Ok(basket);
    }

    private static async Task<Ok<BasketDto>> AddItemAsync(
        AddBasketItemRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        await sender.Send(
            new AddBasketItemCommand(
                userId,
                request.Sku,
                request.Quantity,
                ReadExpectedVersion(httpContext.Request)),
            cancellationToken);
        return await ReadAfterMutationAsync(userId, httpContext, sender, cancellationToken);
    }

    private static async Task<Ok<BasketDto>> SetItemQuantityAsync(
        string sku,
        SetBasketItemQuantityRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        await sender.Send(
            new SetBasketItemQuantityCommand(
                userId,
                sku,
                request.Quantity,
                ReadExpectedVersion(httpContext.Request)),
            cancellationToken);
        return await ReadAfterMutationAsync(userId, httpContext, sender, cancellationToken);
    }

    private static async Task<Ok<BasketDto>> RemoveItemAsync(
        string sku,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        await sender.Send(
            new RemoveBasketItemCommand(userId, sku, ReadExpectedVersion(httpContext.Request)),
            cancellationToken);
        return await ReadAfterMutationAsync(userId, httpContext, sender, cancellationToken);
    }

    private static async Task<Ok<BasketDto>> ReplaceCouponsAsync(
        ReplaceBasketCouponsRequest request,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        await sender.Send(
            new ReplaceBasketCouponsCommand(
                userId,
                request.CouponCodes,
                ReadExpectedVersion(httpContext.Request)),
            cancellationToken);
        return await ReadAfterMutationAsync(userId, httpContext, sender, cancellationToken);
    }

    private static async Task<Ok<BasketDto>> ClearAsync(
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(httpContext.User);
        await sender.Send(
            new ClearBasketCommand(userId, ReadExpectedVersion(httpContext.Request)),
            cancellationToken);
        return await ReadAfterMutationAsync(userId, httpContext, sender, cancellationToken);
    }

    private static async Task<Ok<BasketDto>> ReadAfterMutationAsync(
        string userId,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var basket = await sender.Send(new GetBasketQuery(userId), cancellationToken);
        SetETag(httpContext.Response, basket.Version);
        return TypedResults.Ok(basket);
    }

    private static string GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub")
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The access token does not identify a user.");

    private static long? ReadExpectedVersion(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var values)
            || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return null;
        }

        var value = values.ToString().Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            value = value[2..].Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version < 0)
        {
            throw new ArgumentException("If-Match must contain a non-negative basket version.");
        }

        return version;
    }

    private static void SetETag(HttpResponse response, long version) =>
        response.Headers.ETag = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";
}

public sealed record AddBasketItemRequest
{
    [Required, MaxLength(80)]
    public required string Sku { get; init; }

    [Range(1, BasketLimits.MaximumSupportedLineQuantity)]
    public required int Quantity { get; init; }
}

public sealed record SetBasketItemQuantityRequest
{
    [Range(1, BasketLimits.MaximumSupportedLineQuantity)]
    public required int Quantity { get; init; }
}

public sealed record ReplaceBasketCouponsRequest
{
    public IReadOnlyList<string> CouponCodes { get; init; } = [];
}
