using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Application.CreateOrder;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.OrderService.Integration;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CreateFlashSaleOrder;

internal sealed class CreateFlashSaleOrderCommandHandler(
    OrderDbContext db,
    IOrderRepository orders,
    IOrderSubmissionLock submissionLock,
    IOrderPricingService pricing,
    ICouponRedemptionStore couponRedemptions,
    IOutboxStore outbox,
    ISagaStore sagas,
    IOptions<OrderCheckoutOptions> options,
    TimeProvider clock) : IRequestHandler<CreateFlashSaleOrderCommand, Guid>
{
    public async Task<Guid> Handle(
        CreateFlashSaleOrderCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        var couponCodes = request.CouponCodes ?? [];
        var shippingAddress = request.ShippingAddress.ToSnapshot();
        var fingerprint = OrderRequestFingerprint.Create(
            request.UserId,
            request.PaymentMethod,
            request.CampaignId,
            [(string.Empty, request.Quantity)],
            couponCodes,
            shippingAddress,
            null,
            null);
        var couponDigests = couponCodes.Select(CouponCode.Digest).ToArray();
        await submissionLock.AcquireAsync(
            new[]
                {
                    OrderSubmissionLockKeys.Idempotency(request.UserId, idempotencyKey),
                    OrderSubmissionLockKeys.FlashSaleBuyer(request.CampaignId, request.UserId)
                }
                .Concat(couponDigests.Select(digest =>
                    OrderSubmissionLockKeys.CouponUser(digest, request.UserId))),
            cancellationToken);

        var idempotentOrder = await db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == request.UserId && order.IdempotencyKey == idempotencyKey)
            .Select(order => new { order.Id, order.FlashSaleCampaignId, order.RequestFingerprint })
            .SingleOrDefaultAsync(cancellationToken);
        if (idempotentOrder is not null)
        {
            if (idempotentOrder.FlashSaleCampaignId != request.CampaignId
                || !string.IsNullOrEmpty(idempotentOrder.RequestFingerprint)
                    && !string.Equals(idempotentOrder.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The idempotency key is already bound to another order request.");
            }
            return idempotentOrder.Id;
        }

        var previousCampaignOrder = await db.Orders
            .AsNoTracking()
            .Where(order =>
                order.UserId == request.UserId
                && order.FlashSaleCampaignId == request.CampaignId)
            .Select(order => new { order.Id, order.RequestFingerprint })
            .SingleOrDefaultAsync(cancellationToken);
        if (previousCampaignOrder is not null)
        {
            if (!string.IsNullOrEmpty(previousCampaignOrder.RequestFingerprint)
                && !string.Equals(previousCampaignOrder.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("This buyer already submitted a different request for the campaign.");
            }
            return previousCampaignOrder.Id;
        }

        var campaign = await db.FlashSaleCampaignProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.CampaignId == request.CampaignId, cancellationToken)
            ?? throw new KeyNotFoundException("Flash-sale campaign was not found.");
        var now = clock.GetUtcNow();
        if (!campaign.AcceptsOrders(now))
            throw new InvalidOperationException("Flash-sale campaign is not accepting orders.");
        if (request.Quantity > campaign.PurchaseLimit)
            throw new InvalidOperationException("The requested quantity exceeds the campaign purchase limit.");

        var product = await db.ProductProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Sku == campaign.Sku && value.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The flash-sale product is unavailable.");
        var pricingItems = new[] { new PricingItem(product, request.Quantity) };
        var price = await pricing.CalculateAsync(
            request.UserId,
            pricingItems,
            couponCodes,
            now,
            cancellationToken);
        var paymentDueOnUtc = now.Add(options.Value.PaymentWindow);
        var order = new Order(
            Guid.NewGuid(),
            request.UserId,
            idempotencyKey,
            fingerprint,
            request.PaymentMethod,
            shippingAddress,
            [new OrderLine(product.Sku, product.Name, request.Quantity, product.Price)],
            price.Discounts,
            product.Currency,
            request.CampaignId,
            null,
            null,
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
            FlashSaleCampaignId = order.FlashSaleCampaignId,
            PricingSnapshotId = order.PricingSnapshotId,
            PricingSnapshotHash = order.PricingSnapshotHash,
            PaymentDueOnUtc = order.PaymentDueOnUtc
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));
    }
}
