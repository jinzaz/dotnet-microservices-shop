using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSA.OrderService.Application.AfterSales;
using MSA.OrderService.Domain;

namespace MSA.CommerceTests;

[TestClass]
public sealed class AfterSalesTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Delivered = Created.AddMinutes(6);

    [TestMethod]
    public void Submit_PartialRefundsOfOneLine_AddUpExactlyToThePaidAmount()
    {
        // 3 件共实付 10.00：按件折算 3.33 / 3.33，最后一件拿走剩余 3.34。
        var order = DeliveredOrder();
        var first = Submit(order, [], AfterSalesType.ReturnAndRefund, 1);
        var second = Submit(order, [first], AfterSalesType.RefundOnly, 1);
        var last = Submit(order, [first, second], AfterSalesType.ReturnAndRefund, 1);

        Assert.AreEqual(3.33m, first.RefundAmount);
        Assert.AreEqual(3.33m, second.RefundAmount);
        Assert.AreEqual(3.34m, last.RefundAmount);
        Assert.ThrowsExactly<AfterSalesRuleException>(() => Submit(order, [first, second, last], AfterSalesType.RefundOnly, 1));
    }

    [TestMethod]
    public void Submit_AfterRejection_ReleasesQuantityWithoutOverRefunding()
    {
        var order = DeliveredOrder();
        var rejected = Submit(order, [], AfterSalesType.ReturnAndRefund, 1);
        var kept = Submit(order, [rejected], AfterSalesType.ReturnAndRefund, 1);
        rejected.Reject("商品已使用，不支持退货", "operator", Delivered.AddHours(1));

        var rest = Submit(order, [rejected, kept], AfterSalesType.ReturnAndRefund, 2);

        Assert.AreEqual(10m, kept.RefundAmount + rest.RefundAmount);
        Assert.AreEqual("商品已使用，不支持退货", rejected.RejectionReason);
    }

    [TestMethod]
    public void Submit_ExchangeHoldsQuantityButRefundsNothing()
    {
        var order = DeliveredOrder();
        var exchange = Submit(order, [], AfterSalesType.Exchange, 2);
        var refund = Submit(order, [exchange], AfterSalesType.RefundOnly, 1);

        Assert.AreEqual(0m, exchange.RefundAmount);
        Assert.AreEqual(3.33m, refund.RefundAmount);
        Assert.ThrowsExactly<AfterSalesRuleException>(() => Submit(order, [exchange, refund], AfterSalesType.Reship, 1));
    }

    [TestMethod]
    public void AllowedTypes_FollowFulfillmentAndReturnWindow()
    {
        var shipped = ConfirmedOrder();
        shipped.Ship("ship", new string('B', 64), "SF", "顺丰", "SF1", null, "operator", Created.AddMinutes(3));
        CollectionAssert.AreEqual(new[] { AfterSalesType.RefundOnly }, AfterSalesRequest.AllowedTypes(shipped, Created.AddMinutes(4)).ToArray());

        var delivered = DeliveredOrder();
        Assert.HasCount(4, AfterSalesRequest.AllowedTypes(delivered, Delivered.AddDays(7)));
        Assert.IsEmpty(AfterSalesRequest.AllowedTypes(delivered, Delivered.AddDays(7).AddSeconds(1)));
        Assert.ThrowsExactly<AfterSalesRuleException>(() =>
            AfterSalesRequest.Submit(Guid.NewGuid(), delivered, [], "late", AfterSalesType.RefundOnly, AfterSalesReason.QualityIssue,
                null, [new AfterSalesItem("SKU-1", 1)], [], Delivered.AddDays(8)));
    }

    [TestMethod]
    public void ReturnAndRefund_FullFlow_IgnoresStaleRefundAcknowledgements()
    {
        var request = Submit(DeliveredOrder(), [], AfterSalesType.ReturnAndRefund, 3);
        var now = Delivered.AddHours(1);

        Assert.IsTrue(request.Approve("上海市退货仓", null, "operator", now));
        Assert.IsFalse(request.Approve("上海市退货仓", null, "operator", now));
        Assert.AreEqual(AfterSalesStatus.AwaitingReturn, request.Status);
        Assert.IsTrue(request.SubmitReturnShipment("顺丰", " sf123 ", "buyer-1", now));
        Assert.AreEqual("SF123", request.ReturnTrackingNumber);
        Assert.IsTrue(request.ConfirmReturnReceived(null, "operator", now));
        Assert.IsTrue(request.StartRefund(null, "operator", now));
        var firstAttempt = request.RefundRequestId!.Value;

        Assert.IsTrue(request.MarkRefundFailed(firstAttempt, "channel down", now));
        Assert.IsTrue(request.StartRefund(null, "operator", now));
        var retry = request.RefundRequestId!.Value;
        Assert.AreNotEqual(firstAttempt, retry);
        Assert.IsFalse(request.MarkRefunded(firstAttempt, now));
        Assert.IsTrue(request.MarkRefunded(retry, now));

        Assert.AreEqual(AfterSalesStatus.Refunded, request.Status);
        Assert.AreEqual(10m, request.RefundAmount);
        Assert.IsFalse(request.IsOpen);
        Assert.HasCount(8, request.History);
    }

    [TestMethod]
    public void RefundOnly_ApprovalStartsRefundAndFullRefundClosesUnshippedOrder()
    {
        var order = ConfirmedOrder();
        var request = Submit(order, [], AfterSalesType.RefundOnly, 3);

        Assert.IsTrue(request.Approve(null, null, "operator", Created.AddMinutes(5)));
        Assert.AreEqual(AfterSalesStatus.Refunding, request.Status);
        Assert.IsNotNull(request.RefundRequestId);
        Assert.IsTrue(order.CloseFulfillmentAfterFullRefund(Created.AddMinutes(6)));
        Assert.AreEqual(OrderFulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.AreEqual(OrderStatus.Confirmed, order.Status);
    }

    [TestMethod]
    public void RefundOnly_BeforeShipment_RestocksEveryUnitOnceRefunded()
    {
        var request = Submit(ConfirmedOrder(), [], AfterSalesType.RefundOnly, 2);
        var now = Created.AddMinutes(5);
        Assert.IsFalse(request.RestockUnshippedGoods(now));

        request.Approve(null, null, "operator", now);
        request.MarkRefunded(request.RefundRequestId!.Value, now);

        Assert.IsTrue(request.RestockUnshippedGoods(now));
        Assert.IsFalse(request.RestockUnshippedGoods(now));
        Assert.AreEqual(ReturnDisposition.Restocked, request.ReturnDisposition);
        var restock = StockEvents(request).Single();
        Assert.AreEqual(AfterSalesStockOperation.Restock, restock.Operation);
        Assert.AreEqual(2, restock.Items.Single().Quantity);
    }

    [TestMethod]
    public void RefundOnly_FullRefundInTransit_ReturnsParcelAndAwaitsWarehouse()
    {
        var order = ConfirmedOrder();
        order.Ship("ship", new string('B', 64), "SF", "顺丰", "SF1", null, "operator", Created.AddMinutes(3));
        var request = Submit(order, [], AfterSalesType.RefundOnly, 3);
        var now = Created.AddMinutes(5);
        request.Approve(null, null, "operator", now);
        request.MarkRefunded(request.RefundRequestId!.Value, now);

        Assert.IsTrue(order.CloseFulfillmentAfterFullRefund(now));
        Assert.AreEqual(OrderFulfillmentStatus.ReturnedToSender, order.FulfillmentStatus);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            order.MarkDelivered("delivered", new string('D', 64), null, "operator", now));
        Assert.IsEmpty(AfterSalesRequest.AllowedTypes(order, now));

        Assert.IsTrue(request.AwaitReturnedParcel(now));
        Assert.AreEqual(ReturnDisposition.Pending, request.ReturnDisposition);
        Assert.IsTrue(request.DisposeReturn([new AfterSalesItem("SKU-1", 3)], null, "warehouse", now));
        Assert.AreEqual(AfterSalesStockOperation.Restock, StockEvents(request).Single().Operation);
    }

    [TestMethod]
    public void Reship_ShipsAfterApprovalAndCustomerCompletes()
    {
        var request = Submit(DeliveredOrder(), [], AfterSalesType.Reship, 1);
        var now = Delivered.AddHours(1);

        Assert.ThrowsExactly<AfterSalesRuleException>(() => request.ShipReplacement("SF", "顺丰", "SF9", null, "operator", now));
        request.Approve(null, null, "operator", now);
        Assert.ThrowsExactly<AfterSalesRuleException>(() => request.ShipReplacement("SF", "顺丰", "SF9", null, "operator", now));
        Assert.IsTrue(request.ApplyStockReservation(true, null, now));
        Assert.IsTrue(request.ShipReplacement("sf", "顺丰", "sf9", null, "operator", now));
        Assert.AreEqual(ReplacementStockStatus.Committed, request.ReplacementStock);
        Assert.IsFalse(request.ShipReplacement("SF", "顺丰", "SF9", null, "operator", now));
        Assert.ThrowsExactly<AfterSalesRuleException>(() => request.Cancel("buyer-1", now));
        Assert.IsTrue(request.Complete("买家已确认收到商品", "buyer-1", now));
        Assert.AreEqual(AfterSalesStatus.Completed, request.Status);
    }

    [TestMethod]
    public void Exchange_HoldsReplacementAtApprovalAndRestocksInspectedReturn()
    {
        var request = Submit(DeliveredOrder(), [], AfterSalesType.Exchange, 3);
        var now = Delivered.AddHours(1);

        request.Approve("上海市退货仓", null, "operator", now);
        Assert.AreEqual(ReplacementStockStatus.Reserving, request.ReplacementStock);
        Assert.AreEqual(AfterSalesStockOperation.Reserve, StockEvents(request).Single().Operation);
        Assert.IsTrue(request.ApplyStockReservation(false, "SKU-1 可用库存不足", now));
        Assert.IsTrue(request.RetryReplacementStock("operator", now));
        Assert.IsTrue(request.ApplyStockReservation(true, null, now));
        Assert.IsFalse(request.ApplyStockReservation(true, null, now));

        request.ConfirmReturnReceived(null, "operator", now);
        Assert.AreEqual(ReturnDisposition.Pending, request.ReturnDisposition);
        Assert.ThrowsExactly<AfterSalesRuleException>(() =>
            request.DisposeReturn([new AfterSalesItem("SKU-1", 2)], null, "warehouse", now));
        Assert.IsTrue(request.DisposeReturn([new AfterSalesItem("SKU-1", 2)], "一件外壳破损", "warehouse", now));
        Assert.AreEqual(ReturnDisposition.PartiallyRestocked, request.ReturnDisposition);
        Assert.ThrowsExactly<AfterSalesRuleException>(() => request.Reject("验货不通过", "operator", now));
        request.ShipReplacement("SF", "顺丰", "SF9", null, "operator", now);

        // 新品出库与退货入库是两笔独立流水：退回的 3 件只有 2 件能再卖。
        var operations = StockEvents(request).Select(value => (value.Operation, value.Items.Sum(item => item.Quantity))).ToArray();
        CollectionAssert.AreEqual(
            new[] { (AfterSalesStockOperation.Reserve, 3), (AfterSalesStockOperation.Reserve, 3), (AfterSalesStockOperation.Restock, 2), (AfterSalesStockOperation.Commit, 3) },
            operations);
    }

    [TestMethod]
    public void Cancel_WhileReplacementHoldIsPending_ReleasesItOnceInventoryAnswers()
    {
        var request = Submit(DeliveredOrder(), [], AfterSalesType.Exchange, 1);
        var now = Delivered.AddHours(1);
        request.Approve("上海市退货仓", null, "operator", now);

        request.Cancel("system", now, "买家超时未寄回商品，售后已自动关闭");
        Assert.AreEqual(ReplacementStockStatus.Reserving, request.ReplacementStock);
        request.ApplyStockReservation(true, null, now);

        Assert.AreEqual(ReplacementStockStatus.Released, request.ReplacementStock);
        Assert.AreEqual(AfterSalesStockOperation.Release, StockEvents(request).Last().Operation);
    }

    [TestMethod]
    public void TimeoutPolicy_SelectsOnlyOverdueRequestsItCanHandle()
    {
        var now = Delivered.AddHours(1);
        var policy = new AfterSalesOptions();
        var refundOnly = Submit(DeliveredOrder(), [], AfterSalesType.RefundOnly, 1);
        var exchange = Submit(DeliveredOrder(), [], AfterSalesType.Exchange, 1);
        var due = policy.Due(now + policy.ReviewTimeout).Compile();

        Assert.IsFalse(policy.Due(now).Compile()(refundOnly));
        Assert.IsTrue(due(refundOnly));
        // 没配置默认退货地址时，退货 / 换货不能被自动同意。
        Assert.IsFalse(due(exchange));
        Assert.IsTrue(new AfterSalesOptions { DefaultReturnAddress = "上海市退货仓" }.Due(now + policy.ReviewTimeout).Compile()(exchange));
        Assert.AreEqual(now + policy.ReviewTimeout, policy.DeadlineFor(refundOnly.Status, refundOnly.Type, refundOnly.InTransitAtSubmission, refundOnly.StatusChangedOnUtc));
    }

    [TestMethod]
    public void TimeoutPolicy_NeverAutoApprovesRefundOnlyWhileParcelIsInTransit()
    {
        var order = ConfirmedOrder();
        order.Ship("ship", new string('B', 64), "SF", "顺丰", "SF1", null, "operator", Created.AddMinutes(3));
        var request = Submit(order, [], AfterSalesType.RefundOnly, 1);
        var policy = new AfterSalesOptions { DefaultReturnAddress = "上海市退货仓" };

        Assert.IsTrue(request.InTransitAtSubmission);
        Assert.IsFalse(policy.Due(request.StatusChangedOnUtc + TimeSpan.FromDays(30)).Compile()(request));
        Assert.IsNull(policy.DeadlineFor(request.Status, request.Type, request.InTransitAtSubmission, request.StatusChangedOnUtc));
    }

    private static AfterSalesStockRequestedDomainEvent[] StockEvents(AfterSalesRequest request) =>
        request.DomainEvents.OfType<AfterSalesStockRequestedDomainEvent>().ToArray();

    private static AfterSalesRequest Submit(Order order, AfterSalesRequest[] existing, AfterSalesType type, int quantity) =>
        AfterSalesRequest.Submit(Guid.NewGuid(), order, existing, Guid.NewGuid().ToString(), type, AfterSalesReason.QualityIssue,
            null, [new AfterSalesItem("SKU-1", quantity)], [], Delivered.AddHours(1));

    private static Order DeliveredOrder()
    {
        var order = ConfirmedOrder();
        order.Ship("ship", new string('B', 64), "SF", "顺丰", "SF1", null, "operator", Created.AddMinutes(3));
        order.MarkInTransit("transit", new string('C', 64), null, "operator", Created.AddMinutes(4));
        order.MarkDelivered("delivered", new string('D', 64), null, "operator", Delivered);
        return order;
    }

    /// <summary>Three units listed at 4.00 with a 2.00 promotion: 10.00 paid for the line.</summary>
    private static Order ConfirmedOrder()
    {
        var order = new Order(
            Guid.NewGuid(), "buyer-1", "order-key", new string('F', 64), "Alipay",
            new ShippingAddressSnapshot(null, "Buyer", "13800000000", "CN", "Province", "City", "District", "Street", null, null),
            [new OrderLine(Guid.NewGuid(), Guid.NewGuid(), null, "SKU-1", "Product", "Variant", "{}", 3, 4m, 2m)],
            [new OrderDiscount(Guid.NewGuid(), Guid.NewGuid(), 1, null, "Promotion", "满减", 2m, "{}")],
            "CNY", null, null, Guid.NewGuid(), new string('A', 64), Created.AddMinutes(20), Created);
        order.InventoryReserved(Guid.NewGuid(), Created.AddMinutes(1));
        order.Confirm(Guid.NewGuid(), 10m, "CNY", new string('A', 64), Created.AddMinutes(2));
        return order;
    }
}
