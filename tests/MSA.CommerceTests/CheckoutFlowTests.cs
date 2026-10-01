using MSA.Redis;
using System.Text.Json;
using Grpc.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MSA.Contracts;
using MSA.Grpc.Contracts.Catalog;
using MSA.Grpc.Contracts.Inventory;
using MSA.Grpc.Contracts.Order;
using MSA.Grpc.Contracts.Payment;
using MSA.Grpc.Contracts.User;
using MSA.OrderService.Application;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.GetOrder;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Application.Realtime;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.OrderService.Infrastructure.Grpc;
using MSA.PaymentCore;
using BasketDomain = MSA.BasketService.Domain;
using BasketInfra = MSA.BasketService.Infrastructure;
using CatalogApp = MSA.CatalogService.Application;
using CatalogDomain = MSA.CatalogService.Domain;
using CatalogGrpc = MSA.CatalogService.Grpc;
using CatalogInfra = MSA.CatalogService.Infrastructure;
using InventoryApp = MSA.InventoryService.Application;
using InventoryDomain = MSA.InventoryService.Domain;
using InventoryGrpc = MSA.InventoryService.Grpc;
using InventoryInfra = MSA.InventoryService.Infrastructure;
using OrderGrpc = MSA.OrderService.Grpc;
using PaymentApp = MSA.PaymentService.Application;
using PaymentDomain = MSA.PaymentService.Domain;
using PaymentGrpc = MSA.PaymentService.Grpc;
using PaymentInfra = MSA.PaymentService.Infrastructure;
using PaymentInfraGrpc = MSA.PaymentService.Infrastructure.Grpc;
using PaymentRealtime = MSA.PaymentService.Application.Realtime;
using UserApp = MSA.UserService.Application;
using UserDomain = MSA.UserService.Domain;
using UserGrpc = MSA.UserService.Grpc;
using UserInfra = MSA.UserService.Infrastructure;

namespace MSA.CommerceTests;

/// <summary>
/// 按业务流程组织的结算用例：购物车 → 报价 → 确认最终价格 / 券 / 地址 → 库存预检 → 建单 → 支付核对 → 读支付状态。
/// 跨服务的每一跳都走真实的 gRPC 契约在进程内对接（调用方网关 → 契约 → 对端应用层 → 对端库），
/// 所以价格映射、SKU 归一化、降级策略是在流程里被真正走到的，而不是各服务各测一个接口。
/// </summary>
[TestClass]
public sealed class CheckoutFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
    private const string Sku = "SKU-MUG-001";
    private const string Buyer = "buyer-1";
    private const string DiscountCode = "SAVE20ABC";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Checkout_PricesAtTheCatalogNotAtTheStaleProjection()
    {
        // 目录已降价到 88，订单侧的投影还停在 120：成交价必须是目录此刻的那一个。
        await using var world = await World.CreateAsync(TestContext.CancellationToken, catalogPrice: 88m, projectionPrice: 120m);

        var quote = await world.Quote([new QuoteItem(Sku, 2)], TestContext.CancellationToken);
        var orderId = await world.Checkout(2, TestContext.CancellationToken);
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);

        Assert.AreEqual(176m, quote.Subtotal, "报价也要用确认后的价格。");
        var order = await world.Order.Orders.Include(value => value.Lines).SingleAsync(TestContext.CancellationToken);
        Assert.AreEqual(orderId, order.Id);
        Assert.AreEqual(88m, order.Lines.Single().UnitPrice);
        Assert.AreEqual(176m, order.Amount);

        var submitted = world.PublishedEvent<OrderSubmittedIntegrationEvent>();
        Assert.AreEqual(176m, submitted.Amount, "下游拿到的也必须是确认价。");
        var saga = await world.Order.SagaStates.SingleAsync(TestContext.CancellationToken);
        Assert.AreEqual(OrderFulfillmentSagaData.AwaitingInventory, saga.State);
    }

    [TestMethod]
    public async Task Checkout_StopsWhenTheCatalogHasAlreadyUnpublishedTheProduct()
    {
        // 投影还说在售，目录已经下架：以目录为准，单不能成。
        await using var world = await World.CreateAsync(TestContext.CancellationToken, published: false);

        var quote = await world.Quote([new QuoteItem(Sku, 1)], TestContext.CancellationToken);
        var rejection = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(1, TestContext.CancellationToken));

        Assert.AreEqual(Sku, quote.UnavailableSkus.Single());
        Assert.AreEqual(0m, quote.Total);
        Assert.Contains("unavailable", rejection.Message);
        await world.AssertNothingWasCheckedOut(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task Checkout_FailsClosedWhenTheCatalogCannotBeReached_ButTheQuoteStillShowsAPrice()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken, catalogPrice: 88m, projectionPrice: 120m);
        world.CatalogOnline = false;

        // 报价是预览：问不到就退回投影，不要让用户连价格都看不到。
        var quote = await world.Quote([new QuoteItem(Sku, 1)], TestContext.CancellationToken);
        // 成交不能将就：宁可让用户重试，也不用可能过期的价格出单。
        var rejection = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(1, TestContext.CancellationToken));

        Assert.AreEqual(120m, quote.Subtotal);
        Assert.IsEmpty(quote.UnavailableSkus);
        Assert.Contains("could not confirm the final prices", rejection.Message);
        await world.AssertNothingWasCheckedOut(TestContext.CancellationToken);

        // 更看重下单成功率的部署可以关掉它，退回按投影定价。
        world.Options.RequireRealtimePriceConfirmation = false;
        await world.Checkout(1, TestContext.CancellationToken);
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);
        Assert.AreEqual(120m, (await world.Order.Orders.SingleAsync(TestContext.CancellationToken)).Amount);
    }

    [TestMethod]
    public async Task Checkout_StopsBeforeCreatingAnOrderWhenInventoryCannotServeTheLine()
    {
        // 库存只有 1 件，要 3 件：在建单之前就拒掉，省掉「建单 → 库存拒绝 → 补偿取消」这一圈。
        await using var world = await World.CreateAsync(TestContext.CancellationToken, onHand: 1);

        var rejection = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(3, TestContext.CancellationToken));

        Assert.Contains("Insufficient inventory", rejection.Message);
        Assert.Contains(Sku, rejection.Message);
        await world.AssertNothingWasCheckedOut(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task FlashSaleCheckout_ReportsTheOfferReasonItWasRejectedFor()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken, onHand: 50);
        // 限购 2 件，这个买家在库存侧已经占了 2 件；订单侧的投影还不知道，所以这一刀只能由库存来下。
        var offerId = await world.OpenOfferAsync(total: 10, perUserLimit: 2, alreadyTaken: 2, TestContext.CancellationToken);

        var overLimit = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(1, TestContext.CancellationToken, offerId));
        // 活动已结束。
        await world.CloseOfferAsync(offerId, TestContext.CancellationToken);
        var closed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(1, TestContext.CancellationToken, offerId));

        Assert.Contains("purchase limit", overLimit.Message);
        Assert.Contains("not accepting orders", closed.Message);
        await world.AssertNothingWasCheckedOut(TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task Checkout_AppliesALiveCouponButRejectsOneTheCatalogHasRevoked()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken);
        await world.IssueCouponAsync(TestContext.CancellationToken);

        // 券还在：折扣照算。
        var orderId = await world.Checkout(1, TestContext.CancellationToken, couponCode: DiscountCode);
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);
        var order = await world.Order.Orders.SingleAsync(TestContext.CancellationToken);

        // 后台把券作废了，订单侧的券投影还没同步 —— 这一刀只能由目录来下。
        await world.RevokeCouponAsync(TestContext.CancellationToken);
        var rejection = await Assert.ThrowsExactlyAsync<CouponRejectedException>(
            () => world.Checkout(1, TestContext.CancellationToken, couponCode: DiscountCode));

        Assert.AreEqual(80m, order.Amount, "100 减 20。");
        Assert.AreEqual(CouponRejectionReason.NotAvailable, rejection.Reason);
        Assert.Contains("Inactive", rejection.Message);
    }

    [TestMethod]
    public async Task Checkout_TakesTheRecipientFromTheUserServiceAndRejectsSomebodyElsesAddress()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken);
        var addressId = await world.AddAddressAsync(Buyer, "张三", "13800000001", TestContext.CancellationToken);
        var foreignAddressId = await world.AddAddressAsync("buyer-2", "李四", "13800000002", TestContext.CancellationToken);

        // 客户端可以伪造收件人，但落单用的是用户服务里的那一份。
        var orderId = await world.Checkout(1, TestContext.CancellationToken, addressId: addressId, recipientName: "伪造的收件人");
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);
        var order = await world.Order.Orders.SingleAsync(TestContext.CancellationToken);

        var rejection = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => world.Checkout(1, TestContext.CancellationToken, addressId: foreignAddressId));

        Assert.AreEqual(orderId, order.Id);
        Assert.AreEqual("张三", order.ShippingAddress.RecipientName);
        Assert.AreEqual("13800000001", order.ShippingAddress.PhoneNumber);
        Assert.Contains("does not belong to this user", rejection.Message);
    }

    [TestMethod]
    public async Task Basket_ShowsTheCatalogPriceAndMarksAnUnpublishedItemUnavailable()
    {
        // 购物车快照停在 120，目录已经降到 88：结算页要显示 88，否则用户到下单那一步会看到价格变了。
        await using var world = await World.CreateAsync(TestContext.CancellationToken, catalogPrice: 88m);
        await world.FillBasketAsync(snapshotPrice: 120m, quantity: 2, TestContext.CancellationToken);

        var basket = await world.ReadBasket(TestContext.CancellationToken);

        Assert.AreEqual(88m, basket.Items.Single().UnitPrice);
        Assert.AreEqual(176m, basket.EstimatedTotals.Single().Amount);
        Assert.IsTrue(basket.Items.Single().IsAvailable);
        Assert.IsTrue(basket.HasPriceChanges, "价格和选购时不一样，要提示用户。");

        // 目录下架后，购物车里的那一行立刻变成不可售，而不是等投影同步。
        await world.UnpublishAsync(TestContext.CancellationToken);
        var afterUnpublish = await world.ReadBasket(TestContext.CancellationToken);
        Assert.IsFalse(afterUnpublish.Items.Single().IsAvailable);
        Assert.IsTrue(afterUnpublish.HasUnavailableItems);
        Assert.IsEmpty(afterUnpublish.EstimatedTotals);
    }

    [TestMethod]
    public async Task PaymentRequest_IsRejectedWhenTheOrderNoLongerMatchesTheEvent()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken);
        var orderId = await world.Checkout(1, TestContext.CancellationToken);
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);
        var order = await world.Order.Orders.SingleAsync(TestContext.CancellationToken);
        var precheck = world.PaymentPrecheck;

        // 事件和订单一致：照常收款。
        var accepted = await precheck.RejectAsync(
            orderId, order.Amount, order.Currency, order.PricingSnapshotHash, TestContext.CancellationToken);
        // 金额被改过（或事件已过期）：不能把这笔钱收进来。
        var wrongAmount = await precheck.RejectAsync(
            orderId, order.Amount + 1m, order.Currency, order.PricingSnapshotHash, TestContext.CancellationToken);
        var wrongSnapshot = await precheck.RejectAsync(
            orderId, order.Amount, order.Currency, "not-the-snapshot-hash", TestContext.CancellationToken);
        var unknownOrder = await precheck.RejectAsync(
            Guid.NewGuid(), order.Amount, order.Currency, null, TestContext.CancellationToken);

        Assert.IsNull(accepted);
        Assert.Contains("payable amount changed", wrongAmount!);
        Assert.Contains("pricing snapshot", wrongSnapshot!);
        Assert.Contains("does not exist", unknownOrder!);
    }

    [TestMethod]
    public async Task OrderDetail_ShowsThePaymentServiceStatusWhileThePaidEventIsStillInFlight()
    {
        await using var world = await World.CreateAsync(TestContext.CancellationToken);
        var orderId = await world.Checkout(1, TestContext.CancellationToken);
        await world.Order.SaveChangesAsync(TestContext.CancellationToken);
        await world.ReserveInventoryAsync(orderId, TestContext.CancellationToken);
        await world.PayAsync(orderId, TestContext.CancellationToken);

        var detail = await world.GetOrder(orderId, TestContext.CancellationToken);

        // 订单自己还停在等待支付（OrderPaid 事件还在路上），但详情页已经能显示支付服务的真实结果。
        Assert.AreEqual(nameof(OrderStatus.AwaitingPayment), detail!.Status);
        Assert.AreEqual(nameof(PaymentDomain.PaymentStatus.Succeeded), detail.LivePaymentStatus);

        // 支付服务问不到时详情页照样出，只是没有实时状态。
        world.PaymentOnline = false;
        var degraded = await world.GetOrder(orderId, TestContext.CancellationToken);
        Assert.IsNull(degraded!.LivePaymentStatus);
    }

    /* ---------------- 流程装置：五个服务的库 + 进程内 gRPC 链路 ---------------- */

    private sealed class World : IAsyncDisposable
    {
        private readonly Guid _productId = Guid.NewGuid();
        private readonly Guid _promotionId = Guid.NewGuid();
        private readonly Guid _couponId = Guid.NewGuid();
        private readonly TestClock _clock = new(Now);
        private Guid _variantId;
        private int _checkouts;

        public required CatalogInfra.CatalogDbContext Catalog { get; init; }
        public required InventoryInfra.InventoryDbContext Inventory { get; init; }
        public required PaymentInfra.PaymentDbContext Payments { get; init; }
        public required UserInfra.UserDbContext Users { get; init; }
        public required BasketInfra.BasketDbContext Baskets { get; init; }
        public required OrderDbContext Order { get; init; }
        public OrderCheckoutOptions Options { get; } = new();
        public bool CatalogOnline { get; set; } = true;
        public bool PaymentOnline { get; set; } = true;

        public static async Task<World> CreateAsync(
            CancellationToken cancellationToken,
            decimal catalogPrice = 100m,
            decimal? projectionPrice = null,
            int onHand = 10,
            bool published = true)
        {
            var world = new World
            {
                Catalog = new CatalogInfra.CatalogDbContext(InMemory<CatalogInfra.CatalogDbContext>(), Mock.Of<IMediator>()),
                Inventory = new InventoryInfra.InventoryDbContext(InMemory<InventoryInfra.InventoryDbContext>(), Mock.Of<IMediator>()),
                Payments = new PaymentInfra.PaymentDbContext(InMemory<PaymentInfra.PaymentDbContext>(), Mock.Of<IMediator>()),
                Users = new UserInfra.UserDbContext(InMemory<UserInfra.UserDbContext>(), Mock.Of<IMediator>()),
                Baskets = new BasketInfra.BasketDbContext(InMemory<BasketInfra.BasketDbContext>(), Mock.Of<IMediator>()),
                Order = new OrderDbContext(InMemory<OrderDbContext>(), Mock.Of<IMediator>())
            };

            var product = new CatalogDomain.Product(
                world._productId, Guid.NewGuid(), Sku, "Mug", "Ceramic mug", catalogPrice, "CNY", "operator", Now.AddDays(-2));
            if (published)
                product.Publish("operator", Now.AddDays(-1));
            world._variantId = product.DefaultVariant.Id;
            world.Catalog.Products.Add(product);
            world.Inventory.StockItems.Add(new InventoryDomain.StockItem(Guid.NewGuid(), Sku, onHand));
            // 订单侧的商品投影是最终一致的副本，可以落后于目录。
            world.Order.ProductProjections.Add(new OrderProductProjection(
                world._variantId, world._productId, Sku, "Mug", "Mug", "{}",
                projectionPrice ?? catalogPrice, "CNY", true, null, 1, Now.AddDays(-1)));

            await world.SaveAllAsync(cancellationToken);
            return world;
        }

        public Task<OrderQuoteDto> Quote(IReadOnlyList<QuoteItem> items, CancellationToken cancellationToken) =>
            new QuoteOrderQueryHandler(Order, Pricing, CatalogPrices, _clock)
                .Handle(new QuoteOrderQuery(Buyer, items, null, null), cancellationToken);

        public Task<Guid> Checkout(
            int quantity,
            CancellationToken cancellationToken,
            Guid? offerId = null,
            string? couponCode = null,
            Guid? addressId = null,
            string recipientName = "收货人") =>
            new CreateOrderCommandHandler(
                    Order,
                    new OrderRepository(Order),
                    Mock.Of<IOrderSubmissionLock>(),
                    Pricing,
                    new PostgresCouponRedemptionStore(Order),
                    CatalogPrices,
                    InventoryChecks,
                    CatalogCoupons,
                    UserAddresses,
                    Order,
                    Order,
                    Microsoft.Extensions.Options.Options.Create(Options),
                    _clock)
                .Handle(
                    new CreateOrderCommand(
                        Buyer,
                        $"idem-{++_checkouts}",
                        [new CreateOrderItem(Sku, quantity, offerId)],
                        "Alipay",
                        new ShippingAddressInput(
                            addressId, recipientName, "13800000000", "CN", "广东省", "深圳市", "南山区", "科技园 1 号", null, null),
                        couponCode is null ? null : [couponCode]),
                    cancellationToken);

        public Task<OrderDto?> GetOrder(Guid orderId, CancellationToken cancellationToken) =>
            new GetOrderQueryHandler(Order, PaymentStatus)
                .Handle(new GetOrderQuery(orderId, Buyer, CanReadAll: false), cancellationToken);

        public Task<MSA.BasketService.Application.BasketDto> ReadBasket(CancellationToken cancellationToken) =>
            new BasketInfra.BasketStore(Baskets, CatalogPrices, _clock).GetAsync(Buyer, cancellationToken);

        public PaymentRealtime.OrderPaymentPrecheck PaymentPrecheck =>
            new(new PaymentInfraGrpc.OrderAuthorityGateway(
                OrderSnapshots, Logger<PaymentInfraGrpc.OrderAuthorityGateway>()));

        /// <summary>Saga 的预占成功那一步：订单从 Submitted 进入 AwaitingPayment。</summary>
        public async Task ReserveInventoryAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var order = await Order.Orders.SingleAsync(value => value.Id == orderId, cancellationToken);
            order.InventoryReserved(Guid.NewGuid(), Now.AddMinutes(1));
            await Order.SaveChangesAsync(cancellationToken);
            Order.ChangeTracker.Clear();
        }

        /// <summary>支付服务收到渠道成功回调，但 OrderPaid 事件还没送达订单服务。</summary>
        public async Task PayAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var order = await Order.Orders.AsNoTracking().SingleAsync(value => value.Id == orderId, cancellationToken);
            var payment = new PaymentDomain.Payment(
                Guid.NewGuid(), orderId, Buyer, order.Amount, order.Currency,
                PaymentChannel.Alipay, Now, Now.AddMinutes(15));
            payment.MarkPaid("provider-tx-1", Now.AddMinutes(2));
            Payments.Payments.Add(payment);
            await Payments.SaveChangesAsync(cancellationToken);
            Payments.ChangeTracker.Clear();
        }

        public async Task<Guid> OpenOfferAsync(
            int total,
            int perUserLimit,
            int alreadyTaken,
            CancellationToken cancellationToken)
        {
            var offerId = Guid.NewGuid();
            var allocationId = Guid.NewGuid();
            Inventory.InventoryAllocations.Add(new InventoryDomain.InventoryAllocation(
                allocationId, offerId, Sku, total, Now.AddHours(-1), Now.AddHours(1), perUserLimit, Now.AddHours(-1)));
            if (alreadyTaken > 0)
            {
                Inventory.InventoryAllocationBuyers.Add(new InventoryDomain.InventoryAllocationBuyer(
                    allocationId, Buyer, alreadyTaken, perUserLimit, Now.AddMinutes(-5)));
            }
            Order.InventoryAllocationProjections.Add(new InventoryAllocationProjection(
                allocationId, offerId, Sku, Now.AddHours(-1), Now.AddHours(1), perUserLimit, Now.AddHours(-1)));
            await SaveAllAsync(cancellationToken);
            return offerId;
        }

        public async Task CloseOfferAsync(Guid offerId, CancellationToken cancellationToken)
        {
            var allocation = await Inventory.InventoryAllocations
                .SingleAsync(value => value.OfferId == offerId, cancellationToken);
            allocation.Close(Now);
            await SaveAllAsync(cancellationToken);
        }

        /// <summary>目录发一张 20 元券，订单侧同步到对应的促销与券投影。</summary>
        public async Task IssueCouponAsync(CancellationToken cancellationToken)
        {
            var promotion = new CatalogDomain.Promotion(
                _promotionId, $"P-{_promotionId:N}", "满减 20", CatalogDomain.PromotionTriggerType.Coupon,
                Now.AddDays(-7), Now.AddDays(7), 1, CatalogDomain.PromotionStackPolicy.Stackable, null,
                "operator", Now.AddDays(-7));
            promotion.AddBenefit("FixedAmountOff", 1, """{"amount":20}""", 0, "operator", Now.AddDays(-7));
            promotion.Publish("operator", Now.AddDays(-7));
            var coupon = new CatalogDomain.Coupon(
                _couponId, _promotionId, DiscountCode, Now.AddDays(-7), Now.AddDays(7), null, null, 3,
                CatalogDomain.CouponQuotaStrategy.SingleRow, null, "operator", Now.AddDays(-7));
            coupon.Activate("operator", Now.AddDays(-7));
            Catalog.Promotions.Add(promotion);
            Catalog.Coupons.Add(coupon);

            Order.PromotionProjections.Add(new PromotionProjection(
                _promotionId, $"P-{_promotionId:N}", "满减 20", "Coupon", "Active", Now.AddDays(-7), Now.AddDays(7),
                1, "Stackable", null, 1, "checksum",
                JsonSerializer.Serialize(Array.Empty<PromotionRuleContract>()),
                JsonSerializer.Serialize(new[] { new PromotionBenefitContract("FixedAmountOff", 1, """{"amount":20}""") }),
                "[]", Now.AddDays(-7)));
            Order.CouponProjections.Add(new CouponProjection(
                _couponId, _promotionId, MSA.OrderService.Application.Pricing.CouponCode.Digest(DiscountCode), $"****{DiscountCode[^4..]}",
                "Active", Now.AddDays(-7), Now.AddDays(7), null, null, 3, "SingleRow", 1, [], Now.AddDays(-7)));
            await SaveAllAsync(cancellationToken);
        }

        /// <summary>后台作废这张券；订单侧的投影故意不更新，以此走到「以目录为准」的那条路径。</summary>
        public async Task RevokeCouponAsync(CancellationToken cancellationToken)
        {
            var coupon = await Catalog.Coupons.SingleAsync(value => value.Id == _couponId, cancellationToken);
            coupon.Disable("operator", Now);
            await SaveAllAsync(cancellationToken);
        }

        public async Task<Guid> AddAddressAsync(
            string userId,
            string recipientName,
            string phoneNumber,
            CancellationToken cancellationToken)
        {
            var addressId = Guid.NewGuid();
            Users.UserAddresses.Add(UserDomain.UserAddress.Create(
                addressId,
                userId,
                new UserDomain.AddressDetails(
                    "家", recipientName, phoneNumber, "CN", "广东省", "深圳市", "南山区", "科技园 2 号", "518000"),
                isDefault: true,
                Now.AddDays(-1)));
            await SaveAllAsync(cancellationToken);
            return addressId;
        }

        public async Task FillBasketAsync(decimal snapshotPrice, int quantity, CancellationToken cancellationToken)
        {
            var selection = new BasketDomain.ProductSelection(
                _productId, _variantId, Sku, "Mug", "Mug", "{}", snapshotPrice, "CNY", true, 1);
            var basket = new BasketDomain.Basket(Guid.NewGuid(), Buyer, Now.AddMinutes(-10));
            basket.AddOrIncreaseItem(selection, quantity, 20, 10, null, Now.AddMinutes(-10));
            Baskets.Baskets.Add(basket);
            Baskets.ProductSnapshots.Add(new BasketDomain.BasketProductSnapshot(
                _productId, _variantId, Sku, "Mug", "Mug", "{}", snapshotPrice, "CNY", true, 1, Now.AddDays(-1)));
            await SaveAllAsync(cancellationToken);
        }

        public async Task UnpublishAsync(CancellationToken cancellationToken)
        {
            var product = await Catalog.Products
                .Include(value => value.Variants)
                .SingleAsync(value => value.Id == _productId, cancellationToken);
            product.Unpublish("operator", Now);
            await SaveAllAsync(cancellationToken);
        }

        public TEvent PublishedEvent<TEvent>()
        {
            var name = typeof(TEvent).AssemblyQualifiedName!;
            var message = Order.OutboxMessages.Local.Single(value => value.EventType == name);
            return JsonSerializer.Deserialize<TEvent>(message.Payload)!;
        }

        public async Task AssertNothingWasCheckedOut(CancellationToken cancellationToken)
        {
            await Order.SaveChangesAsync(cancellationToken);
            Assert.IsFalse(await Order.Orders.AnyAsync(cancellationToken), "被拒的结算不能留下订单。");
            Assert.IsFalse(await Order.OutboxMessages.AnyAsync(cancellationToken), "被拒的结算不能发出集成事件。");
            Assert.IsFalse(await Order.SagaStates.AnyAsync(cancellationToken), "被拒的结算不能启动 Saga。");
        }

        private OrderPricingService Pricing => new(
            Order,
            [new MinimumOrderAmountRule(), new MinimumItemQuantityRule()],
            [new PercentageOffBenefit(), new FixedAmountOffBenefit()]);

        private ICatalogPriceAuthority CatalogPrices => new CatalogPriceAuthorityGateway(
            new InProcessCatalogPricingClient(Catalog, () => CatalogOnline), Logger<CatalogPriceAuthorityGateway>());

        private ICouponAuthority CatalogCoupons => new CouponAuthorityGateway(
            new InProcessCouponQueryClient(Catalog, _clock), Logger<CouponAuthorityGateway>());

        private IInventoryAvailabilityGateway InventoryChecks => new MSA.OrderService.Infrastructure.Grpc.InventoryAvailabilityGateway(
            new InProcessInventoryClient(Inventory, _clock), Logger<MSA.OrderService.Infrastructure.Grpc.InventoryAvailabilityGateway>());

        private IPaymentStatusGateway PaymentStatus => new PaymentStatusGateway(
            new InProcessPaymentClient(Payments, () => PaymentOnline), Logger<PaymentStatusGateway>());

        private IUserAddressAuthority UserAddresses => new UserAddressAuthorityGateway(
            new InProcessUserAddressClient(Users), Logger<UserAddressAuthorityGateway>());

        private OrderQuery.OrderQueryClient OrderSnapshots => new InProcessOrderQueryClient(Order);

        private async Task SaveAllAsync(CancellationToken cancellationToken)
        {
            foreach (var db in new DbContext[] { Catalog, Inventory, Payments, Users, Baskets, Order })
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in new DbContext[] { Catalog, Inventory, Payments, Users, Baskets, Order })
                await db.DisposeAsync();
        }
    }

    private static DbContextOptions<TContext> InMemory<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static Microsoft.Extensions.Logging.ILogger<T> Logger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;

    /* ---------------- 进程内的 gRPC 链路：真实契约 + 真实对端处理器 ---------------- */

    /// <summary>把生成的客户端桩接到对端的 gRPC 服务上，两侧的契约映射都会被真正走一遍。</summary>
    private sealed class InProcessCatalogPricingClient(CatalogInfra.CatalogDbContext catalog, Func<bool> online)
        : CatalogPricing.CatalogPricingClient
    {
        public override AsyncUnaryCall<ConfirmPricesResponse> ConfirmPricesAsync(
            ConfirmPricesRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            if (!online())
                throw new RpcException(new Status(StatusCode.Unavailable, "catalog is unreachable"));
            var service = new CatalogGrpc.CatalogPricingGrpcService(
                InProcessGrpc.Sender<CatalogApp.ConfirmSkuPricesQuery, CatalogApp.ConfirmedSkuPrices>(
                    new CatalogApp.ConfirmSkuPricesQueryHandler(catalog, NullCacheStore.Instance)));
            return InProcessGrpc.Call(service.ConfirmPrices(request, InProcessGrpc.Context(cancellationToken)));
        }
    }

    private sealed class InProcessCouponQueryClient(CatalogInfra.CatalogDbContext catalog, TimeProvider clock)
        : CouponQuery.CouponQueryClient
    {
        public override AsyncUnaryCall<ConfirmCouponsResponse> ConfirmCouponsAsync(
            ConfirmCouponsRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var service = new CatalogGrpc.CouponQueryGrpcService(
                InProcessGrpc.Sender<CatalogApp.ConfirmCouponsQuery, CatalogApp.ConfirmedCoupons>(
                    new CatalogApp.ConfirmCouponsQueryHandler(catalog, clock)));
            return InProcessGrpc.Call(service.ConfirmCoupons(request, InProcessGrpc.Context(cancellationToken)));
        }
    }

    private sealed class InProcessInventoryClient(InventoryInfra.InventoryDbContext inventory, TimeProvider clock)
        : InventoryAvailability.InventoryAvailabilityClient
    {
        public override AsyncUnaryCall<CheckAvailabilityResponse> CheckAvailabilityAsync(
            CheckAvailabilityRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var service = new InventoryGrpc.InventoryAvailabilityGrpcService(
                InProcessGrpc.Sender<InventoryApp.CheckAvailabilityQuery, InventoryApp.AvailabilitySnapshot>(
                    new InventoryApp.CheckAvailabilityQueryHandler(
                        new InventoryApp.InventoryReadModel(inventory, NullCacheStore.Instance, clock), inventory, clock)),
                new InMemoryChangeFeed(),
                Microsoft.Extensions.Options.Options.Create(new InventoryGrpc.OfferStreamOptions()),
                Logger<InventoryGrpc.InventoryAvailabilityGrpcService>());
            return InProcessGrpc.Call(service.CheckAvailability(request, InProcessGrpc.Context(cancellationToken)));
        }
    }

    private sealed class InProcessPaymentClient(PaymentInfra.PaymentDbContext payments, Func<bool> online)
        : PaymentQuery.PaymentQueryClient
    {
        public override AsyncUnaryCall<GetOrderPaymentResponse> GetOrderPaymentAsync(
            GetOrderPaymentRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            if (!online())
                throw new RpcException(new Status(StatusCode.DeadlineExceeded, "payment is slow"));
            var service = new PaymentGrpc.PaymentQueryGrpcService(
                InProcessGrpc.Sender<PaymentApp.GetPaymentByOrderQuery, PaymentApp.PaymentDto?>(
                    new PaymentApp.GetPaymentByOrderHandler(payments, NullCacheStore.Instance)));
            return InProcessGrpc.Call(service.GetOrderPayment(request, InProcessGrpc.Context(cancellationToken)));
        }
    }

    private sealed class InProcessUserAddressClient(UserInfra.UserDbContext users)
        : UserAddressQuery.UserAddressQueryClient
    {
        public override AsyncUnaryCall<GetAddressResponse> GetAddressAsync(
            GetAddressRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var service = new UserGrpc.UserAddressGrpcService(
                InProcessGrpc.Sender<UserApp.Addresses.GetUserAddressQuery, UserApp.UserAddressResponse?>(
                    new UserApp.Addresses.GetUserAddressQueryHandler(new UserInfra.UserStore(users))));
            return InProcessGrpc.Call(service.GetAddress(request, InProcessGrpc.Context(cancellationToken)));
        }
    }

    private sealed class InProcessOrderQueryClient(OrderDbContext orders) : OrderQuery.OrderQueryClient
    {
        public override AsyncUnaryCall<GetOrderSnapshotResponse> GetOrderSnapshotAsync(
            GetOrderSnapshotRequest request,
            Metadata? headers = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            var service = new OrderGrpc.OrderQueryGrpcService(
                InProcessGrpc.Sender<GetOrderSnapshotQuery, OrderSnapshotDto?>(new GetOrderSnapshotQueryHandler(orders)));
            return InProcessGrpc.Call(service.GetOrderSnapshot(request, InProcessGrpc.Context(cancellationToken)));
        }
    }
}
