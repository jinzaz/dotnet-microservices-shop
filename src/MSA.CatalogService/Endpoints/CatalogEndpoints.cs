using MSA.CatalogService.Application;

namespace MSA.CatalogService.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Published catalog data is public so anonymous shoppers can browse; handlers only expose
        // published, active products and active categories.
        endpoints.MapGet("/catalog/products", async (
            ISender sender,
            int skip = 0,
            int take = 20,
            string? search = null,
            Guid? categoryId = null,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListProductsQuery(skip, take, search, categoryId), cancellationToken)))
            .AllowAnonymous();

        endpoints.MapGet("/catalog/products/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetProductQuery(id), cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }).AllowAnonymous();

        endpoints.MapGet("/catalog/categories", async (
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var categories = await sender.Send(new ListCategoriesQuery(0, 200, "Active"), cancellationToken);
            return Results.Ok(categories.Items.Select(category =>
                new PublicCategoryDto(category.Id, category.Code, category.Name)));
        }).AllowAnonymous();

        endpoints.MapGet("/catalog/promotions", async (
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListStorefrontPromotionsQuery(), cancellationToken)))
            .AllowAnonymous();

        return endpoints;
    }
}
