using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.OrderService.Integration;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CreateOrder;

internal sealed class CreateOrderCommandHandler(
    OrderDbContext db,
    IOrderRepository orders,
    IOrderSubmissionLock submissionLock,
    IOrderPricingService pricing,
    ICouponRedemptionStore couponRedemptions,
    IOutboxStore outbox,
    ISagaStore sagas,
    IOptions<OrderCheckoutOptions> options,
    TimeProvider clock) : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        var couponCodes = request.CouponCodes ?? [];
        var shippingAddress = request.ShippingAddress.ToSnapshot();
        var fingerprint = OrderRequestFingerprint.Create(
            request.UserId,
            request.PaymentMethod,
            request.Items.Select(item => (item.Sku, item.Quantity, item.OfferId)),
            couponCodes,
            shippingAddress,
            request.SourceBasketId,
            request.SourceBasketVersion);
        var couponDigests = couponCodes.Select(CouponCode.Digest).ToArray();

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
            couponCodes,
            now,
            cancellationToken);

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
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
    }
}

public sealed class OrderCheckoutOptions
{
    public const string SectionName = "OrderCheckout";
    public TimeSpan PaymentWindow { get; set; } = TimeSpan.FromMinutes(15);
}
