using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.InventoryService.Application;
using MSA.InventoryService.Endpoints;

namespace MSA.CommerceTests;

[TestClass]
public sealed class InventoryEndpointTests
{
    [TestMethod]
    public async Task InventoryRoutes_SeparateCustomerAndAdminReadWritePolicies()
    {
        await using var probe = CreateProbe();

        Assert.IsTrue(probe.Routes.Count >= 10);
        foreach (var route in probe.Routes)
        {
            var path = route.RoutePattern.RawText!.Trim('/');
            var method = route.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single();
            var policy = path.StartsWith("inventory/admin/", StringComparison.Ordinal)
                ? method == "GET" ? "inventory.admin.read" : "inventory.admin.write"
                : "inventory-api";
            EndpointProbe.AssertPolicy(route, policy);
        }

        EndpointProbe.AssertPolicy(probe.Find("GET", "inventory/{sku}"), "inventory-api");
        EndpointProbe.AssertPolicy(probe.Find("GET", "inventory/admin/stock"), "inventory.admin.read");
        EndpointProbe.AssertPolicy(probe.Find("POST", "inventory/admin/stock/adjustments"), "inventory.admin.write");
    }

    [TestMethod]
    public async Task Availability_MissingSku_Returns404()
    {
        await using var probe = CreateProbe();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetInventoryAvailabilityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryAvailabilityDto?)null);

        var response = await probe.InvokeAsync("GET", "inventory/{sku}",
            routeValues: new Dictionary<string, string> { ["sku"] = "SKU-1" });

        Assert.AreEqual(StatusCodes.Status404NotFound, response.Response.StatusCode);
        probe.Sender.Verify(sender => sender.Send(
            It.Is<GetInventoryAvailabilityQuery>(query => query.Sku == "SKU-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Availability_ExistingSku_ReturnsDurableBalance()
    {
        await using var probe = CreateProbe();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetInventoryAvailabilityQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryAvailabilityDto("SKU-1", 10, 3, 7, 2));

        var response = await probe.InvokeAsync("GET", "inventory/{sku}",
            routeValues: new Dictionary<string, string> { ["sku"] = "SKU-1" });

        Assert.AreEqual(StatusCodes.Status200OK, response.Response.StatusCode);
        using var json = await System.Text.Json.JsonDocument.ParseAsync(response.Response.Body);
        Assert.AreEqual(7, json.RootElement.GetProperty("available").GetInt32());
        Assert.AreEqual(2L, json.RootElement.GetProperty("version").GetInt64());
    }

    [TestMethod]
    public async Task AdminList_NegativeLowStockThreshold_ReturnsValidationProblemWithoutQuery()
    {
        await using var probe = CreateProbe();

        var response = await probe.InvokeAsync("GET", "inventory/admin/stock", query: "?lowStockThreshold=-1");

        Assert.AreEqual(StatusCodes.Status400BadRequest, response.Response.StatusCode);
        probe.Sender.Verify(sender => sender.Send(
            It.IsAny<ListInventoryStockQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AdminAdjustStock_ForwardsIdempotencyAndOperatorIdentity()
    {
        await using var probe = CreateProbe();
        var movementId = Guid.NewGuid();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<AdjustStockCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdjustStockResult(movementId, Guid.NewGuid(), "SKU-1", 9, 2, 7, false));

        var response = await probe.InvokeAsync("POST", "inventory/admin/stock/adjustments",
            body: new AdjustStockRequest { Sku = "SKU-1", QuantityDelta = -1, Reason = "Damaged" },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = "adjust-1" },
            userId: "stock-admin");

        Assert.AreEqual(StatusCodes.Status200OK, response.Response.StatusCode);
        using var json = await System.Text.Json.JsonDocument.ParseAsync(response.Response.Body);
        Assert.AreEqual(movementId.ToString(), json.RootElement.GetProperty("movementId").GetGuid().ToString());
        probe.Sender.Verify(sender => sender.Send(
            It.Is<AdjustStockCommand>(command =>
                command.Sku == "SKU-1" && command.QuantityDelta == -1
                && command.IdempotencyKey == "adjust-1" && command.OperatorId == "stock-admin"
                && command.Reason == "Damaged"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EndpointProbe CreateProbe()
    {
        var probe = new EndpointProbe(typeof(InventoryAvailabilityDto),
            "MSA.InventoryService.Endpoints.InventoryEndpoints", "MapInventoryEndpoints");
        probe.Map(typeof(InventoryAvailabilityDto),
            "MSA.InventoryService.Endpoints.InventoryAdminEndpoints", "MapInventoryAdminEndpoints");
        probe.Map(typeof(InventoryAvailabilityDto),
            "MSA.InventoryService.Endpoints.InventoryAllocationEndpoints", "MapInventoryAllocationEndpoints");
        probe.Map(typeof(InventoryAvailabilityDto),
            "MSA.InventoryService.Endpoints.InventoryAllocationEndpoints", "MapFlashSaleEndpoints");
        probe.Map(typeof(InventoryAvailabilityDto),
            "MSA.InventoryService.Endpoints.InventoryAllocationEndpoints", "MapFlashSaleAdminEndpoints");
        return probe;
    }
}
