using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.BasketService.Application;
using MSA.BasketService.Endpoints;

namespace MSA.CommerceTests;

[TestClass]
public sealed class BasketEndpointTests
{
    [TestMethod]
    public async Task BasketRoutes_RequireCustomerPolicyAndExposeNoAdminSurface()
    {
        await using var probe = CreateProbe();

        Assert.AreEqual(6, probe.Routes.Count);
        foreach (var route in probe.Routes)
        {
            EndpointProbe.AssertPolicy(route, "basket-api");
            Assert.IsFalse(route.RoutePattern.RawText!.Contains("/admin", StringComparison.Ordinal));
        }

        EndpointProbe.AssertPolicy(probe.Find("GET", "basket"), "basket-api");
        EndpointProbe.AssertPolicy(probe.Find("POST", "basket/items"), "basket-api");
        EndpointProbe.AssertPolicy(probe.Find("DELETE", "basket"), "basket-api");
    }

    [TestMethod]
    public async Task GetBasket_AuthenticatedUser_ReturnsVersionAsETag()
    {
        await using var probe = CreateProbe();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetBasketQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BasketDto.Empty() with { Version = 3 });

        var response = await probe.InvokeAsync("GET", "basket", userId: "buyer-1");

        Assert.AreEqual(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.AreEqual("\"3\"", response.Response.Headers.ETag.ToString());
        using var json = await System.Text.Json.JsonDocument.ParseAsync(response.Response.Body);
        Assert.AreEqual(3L, json.RootElement.GetProperty("version").GetInt64());
        probe.Sender.Verify(sender => sender.Send(
            It.Is<GetBasketQuery>(query => query.UserId == "buyer-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddItem_WeakIfMatch_ForwardsExpectedVersionAndReturnsFreshETag()
    {
        await using var probe = CreateProbe();
        var basketId = Guid.NewGuid();
        probe.Sender.Setup(sender => sender.Send(It.IsAny<AddBasketItemCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketMutationResult(basketId, 4));
        probe.Sender.Setup(sender => sender.Send(It.IsAny<GetBasketQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BasketDto.Empty() with { BasketId = basketId, Version = 4 });

        var response = await probe.InvokeAsync("POST", "basket/items",
            body: new AddBasketItemRequest { Sku = "SKU-1", Quantity = 2 },
            headers: new Dictionary<string, string> { ["If-Match"] = "W/\"3\"" },
            userId: "buyer-1");

        Assert.AreEqual(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.AreEqual("\"4\"", response.Response.Headers.ETag.ToString());
        probe.Sender.Verify(sender => sender.Send(
            It.Is<AddBasketItemCommand>(command => command.UserId == "buyer-1"
                && command.Sku == "SKU-1" && command.Quantity == 2 && command.ExpectedVersion == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AddItem_InvalidIfMatch_RejectsBeforeSendingCommand()
    {
        await using var probe = CreateProbe();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => probe.InvokeAsync("POST", "basket/items",
            body: new AddBasketItemRequest { Sku = "SKU-1", Quantity = 2 },
            headers: new Dictionary<string, string> { ["If-Match"] = "not-a-version" },
            userId: "buyer-1"));

        probe.Sender.Verify(sender => sender.Send(
            It.IsAny<AddBasketItemCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static EndpointProbe CreateProbe() => new(
        typeof(BasketDto), "MSA.BasketService.Endpoints.BasketEndpoints", "MapBasketEndpoints");
}
