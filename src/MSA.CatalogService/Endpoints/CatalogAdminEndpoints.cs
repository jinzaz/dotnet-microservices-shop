using System.Security.Claims;
using MSA.CatalogService.Application;
using MSA.CatalogService.Domain;

namespace MSA.CatalogService.Endpoints;

internal static class CatalogAdminEndpoints
{
    public static IEndpointRouteBuilder MapCatalogAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/catalog/admin")
            .RequireAuthorization("catalog.admin.read");
        var write = endpoints.MapGroup("/catalog/admin")
            .RequireAuthorization("catalog.admin.write");

        read.MapGet("/categories", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListCategoriesQuery(skip, take, status), cancellationToken)));

        write.MapPost("/categories", async (
            CreateAdminCategoryRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreateCategoryAdminCommand(request.Code, request.Name, GetActor(context)),
                cancellationToken);
            return Results.Created($"/api/catalog/admin/categories/{id}", new { id });
        });

        write.MapPut("/categories/{id:guid}/name", async (
            Guid id,
            RenameCategoryRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new RenameCategoryCommand(id, request.Name, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/categories/{id:guid}/activate", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ActivateCategoryCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/categories/{id:guid}/archive", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ArchiveCategoryCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        read.MapGet("/products", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? search = null,
            string? status = null,
            Guid? categoryId = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(
                new ListAdminProductsQuery(skip, take, search, status, categoryId),
                cancellationToken)));

        read.MapGet("/products/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetAdminProductQuery(id), cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        write.MapPost("/products", async (
            CreateProductRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreateDraftProductCommand(
                    request.CategoryId,
                    request.Sku,
                    request.Name,
                    request.Description,
                    request.Price,
                    request.Currency,
                    GetActor(context)),
                cancellationToken);
            return Results.Created($"/api/catalog/admin/products/{id}", new { id });
        });

        write.MapPut("/products/{id:guid}/details", async (
            Guid id,
            UpdateProductDetailsRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new UpdateProductDetailsCommand(id, request.Name, request.Description, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/price", async (
            Guid id,
            ChangeProductPriceRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new ChangeProductPriceCommand(id, request.Price, request.Currency, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/variants", async (
            Guid id,
            CreateProductVariantRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var variantId = await sender.Send(new CreateProductVariantCommand(
                id,
                request.Sku,
                request.DisplayName,
                request.AttributesJson,
                request.Barcode,
                request.Price,
                request.Currency,
                request.IsActive,
                GetActor(context)), cancellationToken);
            return Results.Created($"/api/catalog/admin/products/{id}/variants/{variantId}", new { id = variantId });
        });

        write.MapPut("/products/{id:guid}/variants/{variantId:guid}", async (
            Guid id,
            Guid variantId,
            UpdateProductVariantRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new UpdateProductVariantCommand(
                id,
                variantId,
                request.DisplayName,
                request.AttributesJson,
                request.Barcode,
                request.Price,
                request.Currency,
                request.IsActive,
                request.ExpectedVersion,
                GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapDelete("/products/{id:guid}/variants/{variantId:guid}", async (
            Guid id,
            Guid variantId,
            long? expectedVersion,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new DeleteProductVariantCommand(id, variantId, expectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/media", async (
            Guid id,
            ProductMediaRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var mediaId = await sender.Send(new AddProductMediaCommand(
                id,
                request.VariantId,
                request.MediaType,
                request.Url,
                request.ThumbnailUrl,
                request.AltText,
                request.SortOrder,
                request.ExpectedVersion,
                GetActor(context)), cancellationToken);
            return Results.Created($"/api/catalog/admin/products/{id}/media/{mediaId}", new { id = mediaId });
        });

        write.MapPut("/products/{id:guid}/media/{mediaId:guid}", async (
            Guid id,
            Guid mediaId,
            ProductMediaRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new UpdateProductMediaCommand(
                id,
                mediaId,
                request.VariantId,
                request.MediaType,
                request.Url,
                request.ThumbnailUrl,
                request.AltText,
                request.SortOrder,
                request.ExpectedVersion,
                GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapDelete("/products/{id:guid}/media/{mediaId:guid}", async (
            Guid id,
            Guid mediaId,
            long? expectedVersion,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new DeleteProductMediaCommand(id, mediaId, expectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/publish", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PublishProductCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/unpublish", async (
            Guid id,
            CatalogStateChangeRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new UnpublishProductCommand(id, request.Reason, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/products/{id:guid}/archive", async (
            Guid id,
            CatalogStateChangeRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new ArchiveProductCommand(id, request.Reason, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        MapPromotionEndpoints(read, write);
        MapCouponEndpoints(read, write);
        return endpoints;
    }

    private static void MapPromotionEndpoints(RouteGroupBuilder read, RouteGroupBuilder write)
    {
        read.MapGet("/promotions", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListPromotionsQuery(skip, take, status), cancellationToken)));

        read.MapGet("/promotions/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var promotion = await sender.Send(new GetPromotionQuery(id), cancellationToken);
            return promotion is null ? Results.NotFound() : Results.Ok(promotion);
        });

        write.MapPost("/promotions", async (
            PromotionDefinitionRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreatePromotionCommand(
                    request.Code,
                    request.Name,
                    request.TriggerType,
                    request.StartsOnUtc,
                    request.EndsOnUtc,
                    request.Priority,
                    request.StackPolicy,
                    request.ExclusiveGroup,
                    request.Rules,
                    request.Benefits,
                    request.Targets,
                    GetActor(context)),
                cancellationToken);
            return Results.Created($"/api/catalog/admin/promotions/{id}", new { id });
        });

        write.MapPut("/promotions/{id:guid}", async (
            Guid id,
            PromotionDefinitionRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new ReplacePromotionDefinitionCommand(
                    id,
                    request.Name,
                    request.StartsOnUtc,
                    request.EndsOnUtc,
                    request.Priority,
                    request.StackPolicy,
                    request.ExclusiveGroup,
                    request.Rules,
                    request.Benefits,
                    request.Targets,
                    request.ExpectedVersion,
                    GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/promotions/{id:guid}/publish", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PublishPromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/promotions/{id:guid}/pause", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PausePromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/promotions/{id:guid}/resume", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ResumePromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/promotions/{id:guid}/end", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new EndPromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapCouponEndpoints(RouteGroupBuilder read, RouteGroupBuilder write)
    {
        read.MapGet("/coupons", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            Guid? promotionId = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListCouponsQuery(skip, take, status, promotionId), cancellationToken)));

        read.MapGet("/coupons/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var coupon = await sender.Send(new GetCouponQuery(id), cancellationToken);
            return coupon is null ? Results.NotFound() : Results.Ok(coupon);
        });

        write.MapPost("/coupons", async (
            CreateCouponRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreateCouponCommand(
                    request.PromotionId,
                    request.Code,
                    request.ValidFromUtc,
                    request.ValidUntilUtc,
                    request.AssignedUserId,
                    request.TotalLimit,
                    request.PerUserLimit,
                    request.QuotaStrategy,
                    request.BucketCount,
                    GetActor(context)),
                cancellationToken);
            return Results.Created($"/api/catalog/admin/coupons/{id}", new { id });
        });

        write.MapPost("/coupons/{id:guid}/activate", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ActivateCouponCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        write.MapPost("/coupons/{id:guid}/disable", async (
            Guid id,
            CatalogStateChangeRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new DisableCouponCommand(id, request.Reason, request.ExpectedVersion, GetActor(context)),
                cancellationToken);
            return Results.NoContent();
        });
    }

    private static string GetActor(HttpContext context) =>
        context.User.FindFirstValue("sub")
        ?? context.User.Identity?.Name
        ?? "catalog-operator";
}

public sealed record CreateProductRequest(
    Guid CategoryId,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    string Currency = "CNY");

public sealed record UpdateProductRequest(
    string Name,
    string Description,
    decimal Price,
    bool IsActive);

public sealed record CreateCategoryRequest(string Name);
public sealed record CreateAdminCategoryRequest(string Code, string Name);
public sealed record RenameCategoryRequest(string Name, long? ExpectedVersion);
public sealed record VersionedCatalogRequest(long? ExpectedVersion);
public sealed record CatalogStateChangeRequest(long? ExpectedVersion, string? Reason);
public sealed record UpdateProductDetailsRequest(string Name, string Description, long? ExpectedVersion);
public sealed record ChangeProductPriceRequest(decimal Price, string Currency, long? ExpectedVersion);

public sealed record CreateProductVariantRequest(
    string Sku,
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    string Currency = "CNY",
    bool IsActive = true);

public sealed record UpdateProductVariantRequest(
    string DisplayName,
    string AttributesJson,
    string? Barcode,
    decimal Price,
    string Currency,
    bool IsActive,
    long? ExpectedVersion = null);

public sealed record ProductMediaRequest(
    Guid? VariantId,
    ProductMediaType MediaType,
    string Url,
    string? ThumbnailUrl,
    string? AltText,
    int SortOrder = 0,
    long? ExpectedVersion = null);

public sealed record PromotionDefinitionRequest(
    string Code,
    string Name,
    string TriggerType,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int Priority,
    string StackPolicy,
    string? ExclusiveGroup,
    IReadOnlyList<PromotionRuleInput> Rules,
    IReadOnlyList<PromotionBenefitInput> Benefits,
    IReadOnlyList<PromotionTargetInput> Targets,
    long? ExpectedVersion = null);

public sealed record CreateCouponRequest(
    Guid PromotionId,
    string Code,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    string? AssignedUserId,
    int? TotalLimit,
    int PerUserLimit = 1,
    string QuotaStrategy = "SingleRow",
    int? BucketCount = null);
