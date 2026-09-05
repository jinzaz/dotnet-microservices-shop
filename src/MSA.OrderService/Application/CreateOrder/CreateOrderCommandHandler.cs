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
        var fingerprint = OrderRequestFingerprint.Create(
            request.UserId,
            request.PaymentMethod,
            null,
            request.Items.Select(item => (item.Sku, item.Quantity)),
            couponCodes);
        var couponDigests = couponCodes.Select(CouponCode.Digest).ToArray();

        await submissionLock.AcquireAsync(
            new[] { OrderSubmissionLockKeys.Idempotency(request.UserId, idempotencyKey) }
                .Concat(couponDigests.Select(digest =>
                    OrderSubmissionLockKeys.CouponUser(digest, request.UserId))),
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

        var requestedItems = request.Items
            .Select(item => new CreateOrderItem(item.Sku.Trim().ToUpperInvariant(), item.Quantity))
            .ToArray();
        var skus = requestedItems.Select(item => item.Sku).ToArray();
        var products = await db.ProductProjections
            .AsNoTracking()
            .Where(product => skus.Contains(product.Sku) && product.IsActive)
            .ToDictionaryAsync(product => product.Sku, StringComparer.Ordinal, cancellationToken);
        if (products.Count != skus.Length)
            throw new InvalidOperationException("One or more products are unavailable.");

        var currencies = products.Values
            .Select(product => product.Currency)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (currencies.Length != 1)
            throw new InvalidOperationException("Mixed currencies are not supported.");

        var now = clock.GetUtcNow();
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
            pricingItems.Select(item => new OrderLine(
                item.Product.Sku,
                item.Product.Name,
                item.Quantity,
                item.Product.Price)),
            price.Discounts,
            currencies[0],
            flashSaleCampaignId: null,
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
                .Select(line => new OrderItemContract(line.Sku, line.Quantity, line.UnitPrice))
                .ToArray(),
            Amount = order.Amount,
            Currency = order.Currency,
            PaymentMethod = order.PaymentMethod,
            PricingSnapshotId = order.PricingSnapshotId,
            PricingSnapshotHash = order.PricingSnapshotHash,
            PaymentDueOnUtc = order.PaymentDueOnUtc
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
