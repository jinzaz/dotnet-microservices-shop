using System.Security.Claims;
using MSA.CatalogService.Application;

namespace MSA.CatalogService.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapCompatibilityEndpoints(endpoints);
        MapAdminEndpoints(endpoints);
        return endpoints;
    }

    private static void MapCompatibilityEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", async (
            ISender sender,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListProductsQuery(skip, take), cancellationToken)))
            .RequireAuthorization();

        endpoints.MapGet("/api/products/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetProductQuery(id), cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }).RequireAuthorization();

        endpoints.MapPost("/api/categories", async (
            CreateCategoryRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new CreateCategoryCommand(request.Name), cancellationToken);
            return Results.Created($"/api/admin/catalog/categories/{id}", new { id });
        }).RequireAuthorization("catalog.write");

        endpoints.MapPost("/api/products", async (
            CreateProductRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreateProductCommand(
                    request.CategoryId,
                    request.Sku,
                    request.Name,
                    request.Description,
                    request.Price,
                    request.Currency),
                cancellationToken);
            return Results.Created($"/api/products/{id}", new { id });
        }).RequireAuthorization("catalog.write");

        endpoints.MapPut("/api/products/{id:guid}", async (
            Guid id,
            UpdateProductRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(
                new UpdateProductCommand(id, request.Name, request.Description, request.Price, request.IsActive),
                cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization("catalog.write");
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/catalog")
            .RequireAuthorization("catalog.write");

        admin.MapGet("/categories", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListCategoriesQuery(skip, take, status), cancellationToken)));

        admin.MapPost("/categories", async (
            CreateAdminCategoryRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new CreateCategoryAdminCommand(request.Code, request.Name, GetActor(context)),
                cancellationToken);
            return Results.Created($"/api/admin/catalog/categories/{id}", new { id });
        });

        admin.MapPut("/categories/{id:guid}/name", async (
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

        admin.MapPost("/categories/{id:guid}/activate", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ActivateCategoryCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/categories/{id:guid}/archive", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ArchiveCategoryCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapGet("/products", async (
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

        admin.MapGet("/products/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetAdminProductQuery(id), cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        admin.MapPost("/products", async (
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
            return Results.Created($"/api/admin/catalog/products/{id}", new { id });
        });

        admin.MapPut("/products/{id:guid}/details", async (
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

        admin.MapPost("/products/{id:guid}/price", async (
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

        admin.MapPost("/products/{id:guid}/publish", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PublishProductCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/products/{id:guid}/unpublish", async (
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

        admin.MapPost("/products/{id:guid}/archive", async (
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

        MapPromotionEndpoints(admin);
        MapCouponEndpoints(admin);
    }

    private static void MapPromotionEndpoints(RouteGroupBuilder admin)
    {
        admin.MapGet("/promotions", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListPromotionsQuery(skip, take, status), cancellationToken)));

        admin.MapGet("/promotions/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var promotion = await sender.Send(new GetPromotionQuery(id), cancellationToken);
            return promotion is null ? Results.NotFound() : Results.Ok(promotion);
        });

        admin.MapPost("/promotions", async (
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
            return Results.Created($"/api/admin/catalog/promotions/{id}", new { id });
        });

        admin.MapPut("/promotions/{id:guid}", async (
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

        admin.MapPost("/promotions/{id:guid}/publish", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PublishPromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/promotions/{id:guid}/pause", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new PausePromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/promotions/{id:guid}/resume", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ResumePromotionCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/promotions/{id:guid}/end", async (
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

    private static void MapCouponEndpoints(RouteGroupBuilder admin)
    {
        admin.MapGet("/coupons", async (
            ISender sender,
            int skip = 0,
            int take = 50,
            string? status = null,
            Guid? promotionId = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListCouponsQuery(skip, take, status, promotionId), cancellationToken)));

        admin.MapGet("/coupons/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var coupon = await sender.Send(new GetCouponQuery(id), cancellationToken);
            return coupon is null ? Results.NotFound() : Results.Ok(coupon);
        });

        admin.MapPost("/coupons", async (
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
            return Results.Created($"/api/admin/catalog/coupons/{id}", new { id });
        });

        admin.MapPost("/coupons/{id:guid}/activate", async (
            Guid id,
            VersionedCatalogRequest request,
            HttpContext context,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            await sender.Send(new ActivateCouponCommand(id, request.ExpectedVersion, GetActor(context)), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/coupons/{id:guid}/disable", async (
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
