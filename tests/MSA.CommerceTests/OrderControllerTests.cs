using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.GetOrders;
using MSA.OrderService.Application.ManageFulfillment;
using MSA.OrderService.Controllers;
using MSA.OrderService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class OrderControllerTests
{
    [TestMethod]
    public void OrderRoutes_SeparateCustomerAndAdminPolicies()
    {
        Assert.AreEqual("order/orders", typeof(OrdersController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.AreEqual("order/admin/orders", typeof(AdminOrdersController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.AreEqual("order/admin/orders/{orderId:guid}/fulfillment",
            typeof(AdminOrderFulfillmentController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.AreEqual("order/flash-sales/{campaignId:guid}/orders",
            typeof(FlashSaleOrdersController).GetCustomAttribute<RouteAttribute>()!.Template);

        foreach (var action in new[] { nameof(OrdersController.Create), nameof(OrdersController.List), nameof(OrdersController.Get) })
            Assert.AreEqual("order-api", typeof(OrdersController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.AreEqual("order-api", typeof(FlashSaleOrdersController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.AreEqual("order.admin.read", typeof(AdminOrdersController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.AreEqual("order.admin.write", typeof(AdminOrderFulfillmentController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [TestMethod]
    public async Task CustomerCreate_UsesAuthenticatedUserAndBasketVersionThenReturnsAccepted()
    {
        var sender = new Mock<ISender>();
        var orderId = Guid.NewGuid();
        var basketId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        sender.Setup(value => value.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderId);
        var controller = WithUser(new OrdersController(sender.Object), "buyer-1");
        var request = new OrdersController.CreateOrderRequest(
            [new OrdersController.CreateOrderItemRequest("SKU-1", 2, offerId)],
            "Alipay", Address(), ["SAVE10"], basketId, 4);

        var result = await controller.Create(request, "order-key", CancellationToken.None);

        var accepted = Assert.IsInstanceOfType<AcceptedAtActionResult>(result);
        Assert.AreEqual(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.AreEqual(nameof(OrdersController.Get), accepted.ActionName);
        Assert.AreEqual(orderId, accepted.RouteValues!["id"]);
        sender.Verify(value => value.Send(
            It.Is<CreateOrderCommand>(command => command.UserId == "buyer-1"
                && command.IdempotencyKey == "order-key" && command.SourceBasketId == basketId
                && command.SourceBasketVersion == 4 && command.Items.Single().OfferId == offerId
                && command.Items.Single().Quantity == 2 && command.CouponCodes!.Single() == "SAVE10"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CustomerList_RestrictsQueryToAuthenticatedBuyer()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(It.IsAny<GetOrdersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OrderListItemDto>)[]);
        var controller = WithUser(new OrdersController(sender.Object), "buyer-1");

        var orders = await controller.List(OrderFulfillmentStatus.Shipped, 5, 10);

        Assert.AreEqual(0, orders.Count);
        sender.Verify(value => value.Send(
            It.Is<GetOrdersQuery>(query => query.RequestingUserId == "buyer-1" && !query.CanReadAll
                && query.UserIdFilter == null && query.FulfillmentStatus == OrderFulfillmentStatus.Shipped
                && query.Skip == 5 && query.Take == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CustomerGet_MissingOrder_QueriesForBuyerAndReturns404()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(It.IsAny<GetOrderQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderDto?)null);
        var controller = WithUser(new OrdersController(sender.Object), "buyer-1");
        var id = Guid.NewGuid();

        var response = await controller.Get(id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(response.Result);
        sender.Verify(value => value.Send(
            It.Is<GetOrderQuery>(query => query.OrderId == id && query.UserId == "buyer-1" && !query.CanReadAll),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AdminList_AllowsCrossUserFilterWithoutImpersonatingBuyer()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(It.IsAny<GetOrdersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<OrderListItemDto>)[]);
        var controller = new AdminOrdersController(sender.Object);

        var orders = await controller.List("buyer-2", OrderFulfillmentStatus.AwaitingShipment, 2, 20);

        Assert.AreEqual(0, orders.Count);
        sender.Verify(value => value.Send(
            It.Is<GetOrdersQuery>(query => query.RequestingUserId == "" && query.CanReadAll
                && query.UserIdFilter == "buyer-2" && query.FulfillmentStatus == OrderFulfillmentStatus.AwaitingShipment
                && query.Skip == 2 && query.Take == 20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AdminShip_ForwardsCarrierAndOperatorToFulfillmentCommand()
    {
        var sender = new Mock<ISender>();
        var orderId = Guid.NewGuid();
        var outcome = new OrderFulfillmentCommandResult(orderId, OrderFulfillmentStatus.Shipped,
            true, "SF", "Shunfeng", "TRACK-1", DateTimeOffset.UtcNow);
        sender.Setup(value => value.Send(It.IsAny<UpdateOrderFulfillmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var controller = WithUser(new AdminOrderFulfillmentController(sender.Object), "order-admin");

        var result = await controller.Ship(orderId,
            new AdminOrderFulfillmentController.ShipOrderRequest("SF", "Shunfeng", "TRACK-1", "packed"),
            "ship-key", CancellationToken.None);

        Assert.AreSame(outcome, result);
        sender.Verify(value => value.Send(
            It.Is<UpdateOrderFulfillmentCommand>(command => command.OrderId == orderId
                && command.TargetStatus == OrderFulfillmentStatus.Shipped
                && command.CarrierCode == "SF" && command.TrackingNumber == "TRACK-1"
                && command.IdempotencyKey == "ship-key" && command.Actor == "order-admin"
                && command.Note == "packed"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task FlashSaleCreate_MissingAllocation_Returns404WithoutCreatingOrder()
    {
        var sender = new Mock<ISender>();
        sender.Setup(value => value.Send(It.IsAny<GetInventoryAllocationQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryAllocationSelection?)null);
        var controller = WithUser(new FlashSaleOrdersController(sender.Object), "buyer-1");
        var campaignId = Guid.NewGuid();

        var response = await controller.Create(campaignId,
            new CreateFlashSaleOrderRequest { Quantity = 1, PaymentMethod = "Alipay", ShippingAddress = Address() },
            "order-key", CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(response.Result);
        sender.Verify(value => value.Send(
            It.Is<GetInventoryAllocationQuery>(query => query.AllocationId == campaignId),
            It.IsAny<CancellationToken>()), Times.Once);
        sender.Verify(value => value.Send(
            It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static T WithUser<T>(T controller, string userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], "unit-test"))
            }
        };
        return controller;
    }

    private static ShippingAddressInput Address() => new(null, "Buyer", "13800000000", "CN",
        "Province", "City", "District", "Street", null, null);

    [TestMethod]
    public void RequestRecords_PutValidationOnConstructorParameters()
    {
        // MVC 校验 record 请求体时只认主构造函数参数上的验证特性；写成 [property: Required] 会在模型绑定时抛
        // InvalidOperationException，被 ApiExceptionHandler 映射成笼统的 409，报价、取消订单、售后接口全部不可用。
        var offenders = typeof(OrdersController).Assembly.GetTypes()
            .Where(type => type.IsNested
                           && typeof(ControllerBase).IsAssignableFrom(type.DeclaringType)
                           && type.GetMethod("<Clone>$") is not null)
            .SelectMany(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => type.GetProperty(parameter.Name!))
                .Where(property => property?.GetCustomAttributes<System.ComponentModel.DataAnnotations.ValidationAttribute>(inherit: false).Any() == true)
                .Select(property => $"{type.DeclaringType!.Name}.{type.Name}.{property!.Name}"))
            .ToList();

        Assert.AreEqual(0, offenders.Count, "Move these validation attributes from [property: ...] to the constructor parameter: " + string.Join(", ", offenders));
    }
}
