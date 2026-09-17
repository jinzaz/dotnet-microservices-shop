using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.CatalogService.Application;
using MSA.CatalogService.Endpoints;

namespace MSA.CommerceTests;

[TestClass]
public sealed class CatalogEndpointTests
{
    [TestMethod]
    public async Task CatalogRoutes_SeparateCustomerAndAdminReadWritePolicies()
    {
        await using var probe = CreateProbe();

        Assert.IsTrue(probe.Routes.Count > 15);
        foreach (var route in probe.Routes)
        {
            var path = route.RoutePattern.RawText!.Trim('/');
            var method = route.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
            var policy = path.StartsWith("catalog/admin/", StringComparison.Ordinal)
                ? method == "GET" ? "catalog.admin.read" : "catalog.admin.write"
                : "catalog-api";
            EndpointProbe.AssertPolicy(route, policy);
        }

        EndpointProbe.AssertPolicy(probe.Find("GET", "catalog/products"), "catalog-api");
        EndpointProbe.AssertPolicy(probe.Find("GET", "catalog/admin/products/{id:guid}"), "catalog.admin.read");
        EndpointProbe.AssertPolicy(probe.Find("POST", "catalog/admin/products/{id:guid}/variants"), "catalog.admin.write");
        EndpointProbe.AssertPolicy(probe.Find("DELETE", "catalog/admin/products/{id:guid}/variants/{variantId:guid}"), "catalog.admin.write");
    }

    [TestMethod]
    public async Task CustomerList_UsesPaginationAndReturnsProducts()
    {
        await using var probe = CreateProbe();
        var product = new ProductDto(Guid.NewGuid(), Guid.NewGuid(), "SKU-1", "Product", "Description",
            12m, "CNY", true, DateTimeOffset.UtcNow);
        probe.Sender.Setup(sender => sender.Send(It.IsAny<ListProductsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProductDto>)[product]);

        var response = await probe.InvokeAsync("GET", "catalog/products", query: "?skip=4&take=7");

        Assert.AreEqual(StatusCodes.Status200OK, response.Response.StatusCode);
        using var json = await System.Text.Json.JsonDocument.ParseAsync(response.Response.Body);
        Assert.AreEqual("SKU-1", json.RootElement[0].GetProperty("sku").GetString());
        probe.Sender.Verify(sender => sender.Send(
            It.Is<ListProductsQuery>(query => query.Skip == 4 && query.Take == 7),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CustomerProduct_NotFound_Returns404()
    {
        await using var probe = CreateProbe();
        var id = Guid.NewGuid();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetProductQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductDto?)null);

        var response = await probe.InvokeAsync("GET", "catalog/products/{id:guid}",
            routeValues: new Dictionary<string, string> { ["id"] = id.ToString() });

        Assert.AreEqual(StatusCodes.Status404NotFound, response.Response.StatusCode);
        probe.Sender.Verify(sender => sender.Send(
            It.Is<GetProductQuery>(query => query.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AdminCreateVariant_ForwardsSkuDetailsAndReturnsCreatedLocation()
    {
        await using var probe = CreateProbe();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<CreateProductVariantCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(variantId);

        var response = await probe.InvokeAsync("POST", "catalog/admin/products/{id:guid}/variants",
            body: new CreateProductVariantRequest("SKU-BLUE", "Blue", "{\"color\":\"blue\"}",
                "BARCODE", 12m, "CNY", true),
            routeValues: new Dictionary<string, string> { ["id"] = productId.ToString() },
            userId: "catalog-admin");

        Assert.AreEqual(StatusCodes.Status201Created, response.Response.StatusCode);
        Assert.AreEqual($"/api/catalog/admin/products/{productId}/variants/{variantId}",
            response.Response.Headers.Location.ToString());
        probe.Sender.Verify(sender => sender.Send(
            It.Is<CreateProductVariantCommand>(command =>
                command.ProductId == productId && command.Sku == "SKU-BLUE"
                && command.DisplayName == "Blue" && command.AttributesJson.Contains("blue")
                && command.Price == 12m && command.Actor == "catalog-admin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AdminProduct_NotFound_Returns404()
    {
        await using var probe = CreateProbe();
        var id = Guid.NewGuid();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetAdminProductQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductDto?)null);

        var response = await probe.InvokeAsync("GET", "catalog/admin/products/{id:guid}",
            routeValues: new Dictionary<string, string> { ["id"] = id.ToString() });

        Assert.AreEqual(StatusCodes.Status404NotFound, response.Response.StatusCode);
        probe.Sender.Verify(sender => sender.Send(
            It.Is<GetAdminProductQuery>(query => query.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EndpointProbe CreateProbe()
    {
        var probe = new EndpointProbe(typeof(ProductDto),
            "MSA.CatalogService.Endpoints.CatalogEndpoints", "MapCatalogEndpoints");
        probe.Map(typeof(ProductDto),
            "MSA.CatalogService.Endpoints.CatalogAdminEndpoints", "MapCatalogAdminEndpoints");
        return probe;
    }
}
