using MSA.CatalogService.Application;

namespace MSA.CatalogService.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/catalog/products", async (
            ISender sender,
            int skip = 0,
            int take = 20,
            CancellationToken cancellationToken = default) =>
            Results.Ok(await sender.Send(new ListProductsQuery(skip, take), cancellationToken)))
            .RequireAuthorization("catalog-api");

        endpoints.MapGet("/catalog/products/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var product = await sender.Send(new GetProductQuery(id), cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        }).RequireAuthorization("catalog-api");

        return endpoints;
    }
}
