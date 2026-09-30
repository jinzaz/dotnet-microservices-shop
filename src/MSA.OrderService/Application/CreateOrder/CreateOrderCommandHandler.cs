using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Application.Pricing;
using MSA.Grpc.Contracts.Catalog;
using MSA.OrderService.Application.Realtime;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CreateOrder;

internal sealed class CreateOrderCommandHandler(
    OrderDbContext db,
    IOrderRepository orders,
    IOrderSubmissionLock submissionLock,
    IOrderPricingService pricing,
    ICouponRedemptionStore couponRedemptions,
    ICatalogPriceAuthority priceAuthority,
    IInventoryAvailabilityGateway availability,
    ICouponAuthority couponAuthority,
    IUserAddressAuthority addresses,
    IOutboxStore outbox,
    ISagaStore sagas,
    IOptions<OrderCheckoutOptions> options,
    TimeProvider clock) : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        var couponCodes = request.CouponCodes ?? [];
        var walletCouponIds = request.WalletCouponIds ?? [];
        // 地址先定稿再算指纹：收货信息以用户服务为准，指纹因此不受客户端改写收件人/电话的影响。
        var shippingAddress = await ResolveShippingAddressAsync(request, cancellationToken);
        var fingerprint = OrderRequestFingerprint.Create(
            request.UserId,
            request.PaymentMethod,
            request.Items.Select(item => (item.Sku, item.Quantity, item.OfferId)),
            couponCodes,
            shippingAddress,
            request.SourceBasketId,
            request.SourceBasketVersion,
            walletCouponIds);
        // Wallet coupons lock on the same digest key as typed codes, so both paths share one per-user quota lock.
        var walletDigests = walletCouponIds.Count == 0
            ? []
            : await db.CouponProjections
                .AsNoTracking()
                .Where(coupon => walletCouponIds.Contains(coupon.Id) && coupon.AssignedUserId == request.UserId)
                .Select(coupon => coupon.CodeDigest)
                .ToArrayAsync(cancellationToken);
        var couponDigests = couponCodes.Select(CouponCode.Digest)
            .Concat(walletDigests)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var lockKeys = new List<string>
        {
            OrderSubmissionLockKeys.Idempotency(request.UserId, idempotencyKey)
        };
        if (request.SourceBasketId is Guid sourceBasketId
            && request.SourceBasketVersion is long sourceBasketVersion)
        {
            lockKeys.Add(OrderSubmissionLockKeys.BasketCheckout(
                request.UserId,
                sourceBasketId,
                sourceBasketVersion));
        }
        lockKeys.AddRange(couponDigests.Select(digest =>
            OrderSubmissionLockKeys.CouponUser(digest, request.UserId)));

        // 同时锁住下单键、购物车版本和券用户配额，覆盖所有可能形成重复订单或超卖的业务键。
        await submissionLock.AcquireAsync(
            lockKeys,
            cancellationToken);

        var existing = await db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == request.UserId && order.IdempotencyKey == idempotencyKey)
            .Select(order => new { order.Id, order.RequestFingerprint })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (!string.IsNullOrEmpty(existing.RequestFingerprint)
                && !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The idempotency key is already bound to a different order request.");
            }
            return existing.Id;
        }

        if (request.SourceBasketId is Guid basketId
            && request.SourceBasketVersion is long basketVersion)
        {
            var existingBasketCheckout = await db.Orders
                .AsNoTracking()
                .Where(order =>
                    order.UserId == request.UserId
                    && order.SourceBasketId == basketId
                    && order.SourceBasketVersion == basketVersion)
                .Select(order => new { order.Id, order.RequestFingerprint })
                .SingleOrDefaultAsync(cancellationToken);
            if (existingBasketCheckout is not null)
            {
                if (!string.Equals(
                        existingBasketCheckout.RequestFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The basket version is already bound to a different order request.");
                }
                return existingBasketCheckout.Id;
            }
        }

        var requestedItems = request.Items
            .Select(item => new CreateOrderItem(item.Sku.Trim().ToUpperInvariant(), item.Quantity, item.OfferId))
            .ToArray();
        var skus = requestedItems.Select(item => item.Sku).ToArray();
        var now = clock.GetUtcNow();
        var products = await db.ProductProjections
            .AsNoTracking()
            .Where(product =>
                skus.Contains(product.Sku)
                && product.IsActive
                && product.UpdatedOnUtc <= now)
            .ToDictionaryAsync(product => product.Sku, StringComparer.Ordinal, cancellationToken);
        if (products.Count != skus.Length)
            throw new InvalidOperationException("One or more products are unavailable.");

        var offeredItems = requestedItems.Where(item => item.OfferId.HasValue).ToArray();
        if (offeredItems.Length > 0)
        {
            var offerIds = offeredItems.Select(item => item.OfferId!.Value).Distinct().ToArray();
            var offeredSkus = offeredItems.Select(item => item.Sku).Distinct(StringComparer.Ordinal).ToArray();
            var allocations = await db.InventoryAllocationProjections
                .AsNoTracking()
                .Where(allocation => offerIds.Contains(allocation.OfferId) && offeredSkus.Contains(allocation.Sku))
                .ToArrayAsync(cancellationToken);
            foreach (var item in offeredItems)
            {
                var allocation = allocations.SingleOrDefault(value =>
                    value.OfferId == item.OfferId && value.Sku == item.Sku)
                    ?? throw new InvalidOperationException($"Offer inventory is unavailable for SKU '{item.Sku}'.");
                if (!allocation.AcceptsOrders(now))
                    throw new InvalidOperationException($"Offer is not accepting orders for SKU '{item.Sku}'.");
                if (item.Quantity > allocation.PerUserLimit)
                    throw new InvalidOperationException($"Offer purchase limit is exceeded for SKU '{item.Sku}'.");
            }
        }

        // 结算价以目录此刻的权威数据为准：投影是最终一致的，改价或下架可能还没同步过来。
        products = await ConfirmPricesAsync(products, now, cancellationToken);
        await EnsureAvailableAsync(requestedItems, request.UserId, cancellationToken);

        var currencies = products.Values
            .Select(product => product.Currency)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (currencies.Length != 1)
            throw new InvalidOperationException("Mixed currencies are not supported.");

        var paymentDueOnUtc = now.Add(options.Value.PaymentWindow);
        var pricingItems = requestedItems
            .Select(item => new PricingItem(products[item.Sku], item.Quantity))
            .ToArray();
        var price = await pricing.CalculateAsync(
            request.UserId,
            pricingItems,
            CouponSelection.From(couponCodes, walletCouponIds),
            now,
            cancellationToken);
        await ConfirmCouponsAsync(price.Coupons, request.UserId, cancellationToken);

        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            request.UserId,
            idempotencyKey,
            fingerprint,
            request.PaymentMethod,
            shippingAddress,
            pricingItems.Select(item =>
            {
                var requestItem = requestedItems.Single(value => value.Sku == item.Product.Sku);
                return new OrderLine(
                    item.Product.ProductId,
                    item.Product.VariantId,
                    requestItem.OfferId,
                    item.Product.Sku,
                    item.Product.ProductName,
                    item.Product.VariantName,
                    item.Product.AttributesJson,
                    item.Quantity,
                    item.Product.Price,
                    item.Product.Price);
            }),
            price.Discounts,
            currencies[0],
            request.SourceBasketId,
            request.SourceBasketVersion,
            Guid.NewGuid(),
            price.SnapshotHash,
            paymentDueOnUtc,
            now);
        orders.Add(order);

        await couponRedemptions.ReserveAsync(
            order.Id,
            order.UserId,
            price.Coupons,
            paymentDueOnUtc,
            now,
            cancellationToken);

        // 订单、券配额、Saga 和 Outbox 由事务行为一次提交，避免任一跨服务步骤脱离本地事实。
        PublishOrderSubmitted(order);
        var saga = SagaState.Start(
            order.Id,
            OrderFulfillmentSagaData.SagaType,
            OrderFulfillmentSagaData.AwaitingInventory,
            JsonSerializer.Serialize(OrderFulfillmentSagaData.Start(order)),
            now);
        saga.ScheduleTimeout(paymentDueOnUtc, now);
        sagas.Add(saga);
        return order.Id;
    }

    /// <summary>
    /// 用目录的实时判定替换投影快照。下游不可用时按配置决定：默认拒单，
    /// 宁可让用户重试，也不要用可能过期的价格成交。
    /// </summary>
    private async Task<Dictionary<string, OrderProductProjection>> ConfirmPricesAsync(
        Dictionary<string, OrderProductProjection> projected,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var confirmation = await priceAuthority.ConfirmAsync(projected.Keys.ToArray(), cancellationToken);
        if (confirmation is null)
        {
            if (options.Value.RequireRealtimePriceConfirmation)
                throw new InvalidOperationException("The catalog could not confirm the final prices; please try again.");
            return projected;
        }

        var confirmed = new Dictionary<string, OrderProductProjection>(projected.Count, StringComparer.Ordinal);
        foreach (var sku in projected.Keys)
        {
            if (!confirmation.Items.TryGetValue(sku, out var authority) || !authority.IsActive)
                throw new InvalidOperationException("One or more products are unavailable.");
            confirmed[sku] = authority.ToPricingSnapshot(now);
        }
        return confirmed;
    }

    /// <summary>
    /// 下单前问一次库存：不占用，只是把注定失败的请求挡在建单之前，
    /// 省掉「建单 → 库存拒绝 → 取消」这一圈，用户也能立刻得到明确原因。
    /// 问不到就放行，真正的把关仍然在 Saga 的预占里。
    /// </summary>
    private async Task EnsureAvailableAsync(
        IReadOnlyList<CreateOrderItem> items,
        string userId,
        CancellationToken cancellationToken)
    {
        var lines = items
            .Select(item => new AvailabilityRequestLine(item.Sku, item.Quantity, item.OfferId))
            .ToArray();
        var snapshot = await availability.CheckAsync(lines, userId, cancellationToken);
        if (snapshot is null || snapshot.AllAvailable)
            return;

        var insufficient = snapshot.Lines.First(line => !line.Sufficient);
        throw new InvalidOperationException(insufficient.Reason switch
        {
            "OfferClosed" => $"Offer is not accepting orders for SKU '{insufficient.Sku}'.",
            "OfferLimitExceeded" => $"Offer purchase limit is exceeded for SKU '{insufficient.Sku}'.",
            "OfferSoldOut" => $"Offer inventory is sold out for SKU '{insufficient.Sku}'.",
            _ => $"Insufficient inventory for SKU '{insufficient.Sku}'."
        });
    }

    /// <summary>
    /// 只带地址 id 时向用户服务取权威收货信息：既确认地址属于下单人，也避免客户端改写收件人与电话。
    /// 问不到就按提交上来的用——地址本来就由前端带过来，不值得为此拒单。
    /// </summary>
    private async Task<ShippingAddressSnapshot> ResolveShippingAddressAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        var submitted = request.ShippingAddress.ToSnapshot();
        if (submitted.SourceAddressId is not Guid addressId)
            return submitted;

        var lookup = await addresses.GetAsync(request.UserId, addressId, cancellationToken);
        if (lookup is null)
            return submitted;
        if (!lookup.Found || lookup.Address is null)
            throw new InvalidOperationException("The shipping address does not belong to this user.");

        var address = lookup.Address;
        return new ShippingAddressSnapshot(
            addressId,
            address.RecipientName,
            address.PhoneNumber,
            address.CountryCode,
            address.Province,
            address.City,
            address.District,
            address.Street,
            null,
            address.PostalCode);
    }

    /// <summary>
    /// 券的作废与改期变化快，投影可能还没同步；成交前向目录确认一次。
    /// 问不到就按投影的结论走——配额与限领仍由本地的领用记录把关。
    /// </summary>
    private async Task ConfirmCouponsAsync(
        IReadOnlyList<AppliedCoupon> coupons,
        string userId,
        CancellationToken cancellationToken)
    {
        if (coupons.Count == 0)
            return;

        var confirmation = await couponAuthority.ConfirmAsync(
            coupons.Select(coupon => coupon.CouponId).ToArray(),
            userId,
            cancellationToken);
        if (confirmation is null)
            return;

        foreach (var coupon in coupons)
        {
            if (confirmation.Items.TryGetValue(coupon.CouponId, out var state) && state.Usable)
                continue;
            throw new CouponRejectedException(
                CouponRejectionReason.NotAvailable,
                $"Coupon is no longer usable ({state?.Reason ?? "Unknown"}).",
                coupon.CouponId);
        }
    }

    private void PublishOrderSubmitted(Order order)
    {
        var integrationEvent = new OrderSubmittedIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Items = order.Lines
                .Select(line => new OrderItemContract(
                    line.Sku,
                    line.Quantity,
                    line.UnitPrice,
                    line.ProductId,
                    line.VariantId,
                    line.OfferId))
                .ToArray(),
            Amount = order.Amount,
            Currency = order.Currency,
            PaymentMethod = order.PaymentMethod,
            PricingSnapshotId = order.PricingSnapshotId,
            PricingSnapshotHash = order.PricingSnapshotHash,
            PaymentDueOnUtc = order.PaymentDueOnUtc,
            SourceBasketId = order.SourceBasketId,
            SourceBasketVersion = order.SourceBasketVersion
        };
        outbox.AddIntegrationEvent(integrationEvent);
    }
}

public sealed class OrderCheckoutOptions
{
    public const string SectionName = "OrderCheckout";
    public TimeSpan PaymentWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 目录确认不到价格时是否拒单。默认拒单：宁可让用户重试，也不要用可能过期的价格成交。
    /// 如果更看重下单成功率，可以关掉它退回按投影定价。
    /// </summary>
    public bool RequireRealtimePriceConfirmation { get; set; } = true;
}
