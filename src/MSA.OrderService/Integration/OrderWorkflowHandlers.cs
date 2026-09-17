using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBus.Abstractions;
using MSA.EventBusOutbox;
using MSA.OrderService.Application;
using MSA.OrderService.Application.CancelOrder;
using MSA.OrderService.Application.Pricing;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Integration;

internal abstract class OrderEventHandler<TEvent>(
    OrderDbContext db,
    IOutboxStore outbox,
    string consumerName) : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await db.TryBeginMessageAsync(
                    integrationEvent.Id,
                    consumerName,
                    cancellationToken))
            {
                await db.RollbackTransactionAsync(cancellationToken);
                return;
            }

            await HandleCoreAsync(integrationEvent, cancellationToken);
            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    protected OrderDbContext Db => db;

    protected abstract Task HandleCoreAsync(
        TEvent integrationEvent,
        CancellationToken cancellationToken);

    protected void Publish(IntegrationEvent integrationEvent) => outbox.AddIntegrationEvent(integrationEvent);
}

internal sealed class ProductChangedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<ProductChangedIntegrationEvent>(db, outbox, "ordering.catalog-product.v1")
{
    // Legacy single-SKU event: the product id doubles as its default variant id.
    protected override Task HandleCoreAsync(
        ProductChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        ProductProjectionUpsert.ApplyAsync(
            Db,
            new OrderProductProjection(
                integrationEvent.ProductId,
                integrationEvent.ProductId,
                integrationEvent.Sku,
                integrationEvent.Name,
                integrationEvent.Name,
                "{}",
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.CategoryId,
                integrationEvent.Revision,
                ProductProjectionUpsert.EffectiveOn(integrationEvent.EffectiveOnUtc, integrationEvent.CreationDate)),
            cancellationToken);
}

internal sealed class ProductVariantChangedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<ProductVariantChangedIntegrationEvent>(db, outbox, "ordering.catalog-product-variant.v1")
{
    protected override Task HandleCoreAsync(
        ProductVariantChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        ProductProjectionUpsert.ApplyAsync(
            Db,
            new OrderProductProjection(
                integrationEvent.VariantId,
                integrationEvent.ProductId,
                integrationEvent.Sku,
                integrationEvent.ProductName,
                integrationEvent.VariantName,
                integrationEvent.AttributesJson,
                integrationEvent.Price,
                integrationEvent.Currency,
                integrationEvent.IsActive,
                integrationEvent.CategoryId,
                integrationEvent.Revision,
                ProductProjectionUpsert.EffectiveOn(integrationEvent.EffectiveOnUtc, integrationEvent.CreationDate)),
            cancellationToken);
}

internal static class ProductProjectionUpsert
{
    public static DateTimeOffset EffectiveOn(DateTimeOffset? effectiveOnUtc, DateTime creationDate) =>
        effectiveOnUtc ?? new DateTimeOffset(DateTime.SpecifyKind(creationDate, DateTimeKind.Utc));

    /// <summary>Adds the incoming snapshot or advances the local copy; stale revisions are ignored by the projection.</summary>
    public static async Task ApplyAsync(
        OrderDbContext db,
        OrderProductProjection incoming,
        CancellationToken cancellationToken)
    {
        var existing = await db.ProductProjections
            .SingleOrDefaultAsync(value => value.VariantId == incoming.VariantId, cancellationToken);
        if (existing is null)
        {
            db.ProductProjections.Add(incoming);
            return;
        }

        existing.Apply(
            incoming.ProductName,
            incoming.VariantName,
            incoming.AttributesJson,
            incoming.Price,
            incoming.Currency,
            incoming.IsActive,
            incoming.CategoryId,
            incoming.SourceRevision,
            incoming.UpdatedOnUtc);
    }
}

internal sealed class PromotionSnapshotChangedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<PromotionSnapshotChangedIntegrationEvent>(db, outbox, "ordering.promotion-snapshot.v1")
{
    protected override async Task HandleCoreAsync(
        PromotionSnapshotChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        var rulesJson = JsonSerializer.Serialize(integrationEvent.Rules);
        var benefitsJson = JsonSerializer.Serialize(integrationEvent.Benefits);
        var targetsJson = JsonSerializer.Serialize(integrationEvent.Targets);
        var promotion = await Db.PromotionProjections
            .SingleOrDefaultAsync(value => value.Id == integrationEvent.PromotionId, cancellationToken);
        if (promotion is null)
        {
            Db.PromotionProjections.Add(new PromotionProjection(
                integrationEvent.PromotionId,
                integrationEvent.Code,
                integrationEvent.Name,
                integrationEvent.TriggerType,
                integrationEvent.Status,
                integrationEvent.StartsOnUtc,
                integrationEvent.EndsOnUtc,
                integrationEvent.Priority,
                integrationEvent.StackPolicy,
                integrationEvent.ExclusiveGroup,
                integrationEvent.Revision,
                integrationEvent.DefinitionChecksum,
                rulesJson,
                benefitsJson,
                targetsJson,
                occurredOnUtc));
            return;
        }

        promotion.Apply(
            integrationEvent.Code,
            integrationEvent.Name,
            integrationEvent.TriggerType,
            integrationEvent.Status,
            integrationEvent.StartsOnUtc,
            integrationEvent.EndsOnUtc,
            integrationEvent.Priority,
            integrationEvent.StackPolicy,
            integrationEvent.ExclusiveGroup,
            integrationEvent.Revision,
            integrationEvent.DefinitionChecksum,
            rulesJson,
            benefitsJson,
            targetsJson,
            occurredOnUtc);
    }
}

internal sealed class CouponSnapshotChangedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<CouponSnapshotChangedIntegrationEvent>(db, outbox, "ordering.coupon-snapshot.v1")
{
    protected override async Task HandleCoreAsync(
        CouponSnapshotChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        var coupon = await Db.CouponProjections
            .Include(value => value.QuotaBuckets)
            .SingleOrDefaultAsync(value => value.Id == integrationEvent.CouponId, cancellationToken);
        if (coupon is null)
        {
            var buckets = integrationEvent.QuotaBuckets.Select(bucket =>
                new CouponQuotaBucket(
                    integrationEvent.CouponId,
                    bucket.BucketNumber,
                    bucket.Capacity));
            Db.CouponProjections.Add(new CouponProjection(
                integrationEvent.CouponId,
                integrationEvent.PromotionId,
                integrationEvent.CodeDigest.ToUpperInvariant(),
                integrationEvent.MaskedCode,
                integrationEvent.Status,
                integrationEvent.ValidFromUtc,
                integrationEvent.ValidUntilUtc,
                integrationEvent.AssignedUserId,
                integrationEvent.TotalLimit,
                integrationEvent.PerUserLimit,
                integrationEvent.QuotaStrategy,
                integrationEvent.Revision,
                buckets,
                occurredOnUtc));
            return;
        }

        coupon.Apply(
            integrationEvent.MaskedCode,
            integrationEvent.Status,
            integrationEvent.ValidFromUtc,
            integrationEvent.ValidUntilUtc,
            integrationEvent.AssignedUserId,
            integrationEvent.TotalLimit,
            integrationEvent.PerUserLimit,
            integrationEvent.QuotaStrategy,
            integrationEvent.Revision,
            occurredOnUtc);
        // 订单服务接收后的配额拓扑不可重建；后续修订可以停用或延长有效期，但不能重置已用计数。
    }
}

internal sealed class FlashSaleCampaignOpenedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<FlashSaleCampaignOpenedIntegrationEvent>(db, outbox, "ordering.flash-sale-opened.v1")
{
    protected override async Task HandleCoreAsync(
        FlashSaleCampaignOpenedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var projection = await Db.InventoryAllocationProjections
            .SingleOrDefaultAsync(value => value.AllocationId == integrationEvent.CampaignId, cancellationToken);
        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        if (projection is null)
        {
            Db.InventoryAllocationProjections.Add(new InventoryAllocationProjection(
                integrationEvent.CampaignId,
                integrationEvent.CampaignId,
                integrationEvent.Sku,
                integrationEvent.StartsOnUtc,
                integrationEvent.EndsOnUtc,
                integrationEvent.PurchaseLimit,
                occurredOnUtc));
            return;
        }

        projection.ApplyOpen(
            integrationEvent.Sku,
            integrationEvent.StartsOnUtc,
            integrationEvent.EndsOnUtc,
            integrationEvent.PurchaseLimit,
            occurredOnUtc);
    }
}

internal sealed class FlashSaleCampaignClosedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<FlashSaleCampaignClosedIntegrationEvent>(db, outbox, "ordering.flash-sale-closed.v1")
{
    protected override async Task HandleCoreAsync(
        FlashSaleCampaignClosedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var projection = await Db.InventoryAllocationProjections
            .SingleOrDefaultAsync(value => value.AllocationId == integrationEvent.CampaignId, cancellationToken);
        if (projection is null)
        {
            Db.InventoryAllocationProjections.Add(
                InventoryAllocationProjection.CreateClosed(
                    integrationEvent.CampaignId,
                    integrationEvent.CampaignId,
                    $"CLOSED:{integrationEvent.CampaignId:N}",
                    integrationEvent.ClosedOnUtc));
            return;
        }
        projection.Close(integrationEvent.ClosedOnUtc);
    }
}

internal sealed class InventoryAllocationOpenedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<InventoryAllocationOpenedIntegrationEvent>(db, outbox, "ordering.inventory-allocation-opened.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryAllocationOpenedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var projection = await Db.InventoryAllocationProjections
            .SingleOrDefaultAsync(value => value.AllocationId == integrationEvent.AllocationId, cancellationToken);
        var occurredOnUtc = new DateTimeOffset(
            DateTime.SpecifyKind(integrationEvent.CreationDate, DateTimeKind.Utc));
        if (projection is null)
        {
            Db.InventoryAllocationProjections.Add(new InventoryAllocationProjection(
                integrationEvent.AllocationId,
                integrationEvent.OfferId,
                integrationEvent.Sku,
                integrationEvent.StartsOnUtc,
                integrationEvent.EndsOnUtc,
                integrationEvent.PerUserLimit,
                occurredOnUtc));
            return;
        }

        if (projection.OfferId != integrationEvent.OfferId)
            throw new InvalidOperationException("Inventory allocation cannot be reassigned to another offer.");
        projection.ApplyOpen(
            integrationEvent.Sku,
            integrationEvent.StartsOnUtc,
            integrationEvent.EndsOnUtc,
            integrationEvent.PerUserLimit,
            occurredOnUtc);
    }
}

internal sealed class InventoryAllocationClosedHandler(OrderDbContext db, IOutboxStore outbox)
    : OrderEventHandler<InventoryAllocationClosedIntegrationEvent>(db, outbox, "ordering.inventory-allocation-closed.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryAllocationClosedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var projection = await Db.InventoryAllocationProjections
            .SingleOrDefaultAsync(value => value.AllocationId == integrationEvent.AllocationId, cancellationToken);
        if (projection is null)
        {
            Db.InventoryAllocationProjections.Add(InventoryAllocationProjection.CreateClosed(
                integrationEvent.AllocationId,
                integrationEvent.OfferId,
                integrationEvent.Sku,
                integrationEvent.ClosedOnUtc));
            return;
        }
        projection.Close(integrationEvent.ClosedOnUtc);
    }
}

internal sealed class InventoryReservedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<InventoryReservedIntegrationEvent>(db, outbox, "ordering.inventory-reserved.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryReservedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            Publish(new ReleaseInventoryIntegrationEvent
            {
                OrderId = order.Id,
                ReservationId = integrationEvent.ReservationId,
                Reason = "Order was cancelled before inventory reservation completed."
            });
            return;
        }
        if (order.Status != OrderStatus.Submitted)
            return;

        var now = clock.GetUtcNow();
        order.InventoryReserved(integrationEvent.ReservationId, now);
        var saga = await sagas.FindAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Order fulfillment saga is missing.");
        var data = OrderFulfillmentSagaData.Parse(saga.Data) with
        {
            ReservationId = integrationEvent.ReservationId
        };
        saga.TransitionTo(
            OrderFulfillmentSagaData.AwaitingPayment,
            JsonSerializer.Serialize(data),
            now);
        saga.ScheduleTimeout(order.PaymentDueOnUtc, now);
        Publish(new PaymentRequestedIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = order.Amount,
            Currency = order.Currency,
            PaymentMethod = order.PaymentMethod,
            PricingSnapshotId = order.PricingSnapshotId,
            PricingSnapshotHash = order.PricingSnapshotHash,
            PaymentDueOnUtc = order.PaymentDueOnUtc
        });
    }
}

internal sealed class InventoryRejectedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    ICouponRedemptionStore couponRedemptions,
    TimeProvider clock)
    : OrderEventHandler<InventoryRejectedIntegrationEvent>(db, outbox, "ordering.inventory-rejected.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryRejectedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        var now = clock.GetUtcNow();
        if (!order.TryCancel(integrationEvent.Reason, now))
            return;

        await couponRedemptions.ReleaseAsync(order.Id, now, cancellationToken);
        var saga = await sagas.FindAsync(order.Id, cancellationToken);
        saga?.Fail(integrationEvent.Reason, now);
        PublishCancelled(order, integrationEvent.Reason);
    }

    private void PublishCancelled(Order order, string reason) =>
        Publish(new OrderCancelledIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Reason = reason
        });
}

internal sealed class PaymentSucceededHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    ICouponRedemptionStore couponRedemptions,
    TimeProvider clock)
    : OrderEventHandler<PaymentSucceededIntegrationEvent>(db, outbox, "ordering.payment-succeeded.v1")
{
    protected override async Task HandleCoreAsync(
        PaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        var saga = await sagas.FindAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Order fulfillment saga is missing.");
        var now = clock.GetUtcNow();

        if (order.Status == OrderStatus.Cancelled || now >= order.PaymentDueOnUtc)
        {
            const string latePaymentReason = "Payment completed after the order payment deadline.";
            var newlyCancelled = order.TryCancel(latePaymentReason, now);
            if (newlyCancelled)
            {
                await couponRedemptions.ReleaseAsync(order.Id, now, cancellationToken);
                Publish(new OrderCancelledIntegrationEvent
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    Reason = latePaymentReason
                });
            }
            order.EnsurePaymentMatchesPriceSnapshot(
                integrationEvent.CapturedAmount ?? order.Amount,
                integrationEvent.Currency ?? order.Currency,
                integrationEvent.PricingSnapshotHash);
            var current = OrderFulfillmentSagaData.Parse(saga.Data);
            var data = current with
            {
                PaymentId = integrationEvent.PaymentId,
                RefundRequestId = current.RefundRequestId ?? Guid.NewGuid(),
                RequiresInventoryCompensation = order.ReservationId.HasValue,
                InventoryCompensated = !order.ReservationId.HasValue || current.InventoryCompensated,
                RequiresPaymentCompensation = true,
                PaymentCompensated = false,
                FailureReason = order.CancellationReason ?? latePaymentReason
            };
            saga.BeginCompensation(
                OrderFulfillmentSagaData.CompensatingRefund,
                JsonSerializer.Serialize(data),
                now);
            saga.ScheduleTimeout(now.AddMinutes(10), now);
            Publish(new RefundPaymentRequestedIntegrationEvent
            {
                RefundRequestId = data.RefundRequestId!.Value,
                OrderId = order.Id,
                PaymentId = integrationEvent.PaymentId,
                Amount = order.Amount,
                Currency = order.Currency,
                Reason = data.FailureReason!
            });
            if (data.RequiresInventoryCompensation
                && !data.InventoryCompensated
                && order.ReservationId is Guid releaseReservationId)
            {
                Publish(new ReleaseInventoryIntegrationEvent
                {
                    OrderId = order.Id,
                    ReservationId = releaseReservationId,
                    Reason = data.FailureReason!
                });
            }
            return;
        }

        if (order.Status == OrderStatus.Confirmed)
            return;
        order.Confirm(
            integrationEvent.PaymentId,
            integrationEvent.CapturedAmount ?? order.Amount,
            integrationEvent.Currency ?? order.Currency,
            integrationEvent.PricingSnapshotHash,
            now);
        await couponRedemptions.ConsumeAsync(order.Id, now, cancellationToken);

        var sagaData = OrderFulfillmentSagaData.Parse(saga.Data) with
        {
            PaymentId = integrationEvent.PaymentId
        };
        saga.TransitionTo(
            OrderFulfillmentSagaData.AwaitingInventoryCommit,
            JsonSerializer.Serialize(sagaData),
            now);
        saga.ScheduleTimeout(now.AddMinutes(5), now);
        if (order.ReservationId is Guid reservationId)
        {
            Publish(new CommitInventoryIntegrationEvent
            {
                OrderId = order.Id,
                ReservationId = reservationId
            });
        }
        Publish(new OrderConfirmedIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = order.Amount,
            Currency = order.Currency
        });
    }
}

internal sealed class InventoryCommittedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<InventoryCommittedIntegrationEvent>(db, outbox, "ordering.inventory-committed.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryCommittedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        if (order.ReservationId != integrationEvent.ReservationId)
            throw new InvalidOperationException("Inventory commit acknowledgement does not match the order reservation.");
        var saga = await sagas.FindAsync(order.Id, cancellationToken);
        if (saga?.Status == SagaStatus.Running)
            saga.Complete(clock.GetUtcNow());
    }
}

internal sealed class PaymentFailedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    ICouponRedemptionStore couponRedemptions,
    TimeProvider clock)
    : OrderEventHandler<PaymentFailedIntegrationEvent>(db, outbox, "ordering.payment-failed.v1")
{
    protected override async Task HandleCoreAsync(
        PaymentFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        var now = clock.GetUtcNow();
        if (!order.TryCancel(integrationEvent.Reason, now))
            return;

        await couponRedemptions.ReleaseAsync(order.Id, now, cancellationToken);
        var saga = await sagas.FindAsync(order.Id, cancellationToken);
        if (saga is not null)
        {
            var data = OrderFulfillmentSagaData.Parse(saga.Data) with
            {
                PaymentId = integrationEvent.PaymentId,
                RequiresInventoryCompensation = order.ReservationId.HasValue,
                InventoryCompensated = !order.ReservationId.HasValue,
                FailureReason = integrationEvent.Reason
            };
            if (order.ReservationId.HasValue)
            {
                saga.BeginCompensation(
                    OrderFulfillmentSagaData.CompensatingInventory,
                    JsonSerializer.Serialize(data),
                    now);
                saga.ScheduleTimeout(now.AddMinutes(5), now);
            }
            else
            {
                saga.Fail(integrationEvent.Reason, now);
            }
        }

        if (order.ReservationId is Guid reservationId)
        {
            Publish(new ReleaseInventoryIntegrationEvent
            {
                OrderId = order.Id,
                ReservationId = reservationId,
                Reason = integrationEvent.Reason
            });
        }
        Publish(new OrderCancelledIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Reason = integrationEvent.Reason
        });
    }
}

internal sealed class InventoryReleasedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<InventoryReleasedIntegrationEvent>(db, outbox, "ordering.inventory-released.v1")
{
    protected override async Task HandleCoreAsync(
        InventoryReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        if (order.ReservationId != integrationEvent.ReservationId)
            throw new InvalidOperationException("Inventory release acknowledgement does not match the order reservation.");
        await OrderCompensation.MarkAsync(order.Id, inventory: true, payment: false, sagas, clock.GetUtcNow(), cancellationToken);
    }
}

internal sealed class PaymentCancelledHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<PaymentCancelledIntegrationEvent>(db, outbox, "ordering.payment-cancelled.v1")
{
    protected override async Task HandleCoreAsync(
        PaymentCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        _ = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        await OrderCompensation.MarkAsync(integrationEvent.OrderId, inventory: false, payment: true, sagas, clock.GetUtcNow(), cancellationToken);
    }
}

internal sealed class PaymentRefundedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<PaymentRefundedIntegrationEvent>(db, outbox, "ordering.payment-refunded.v1")
{
    protected override async Task HandleCoreAsync(
        PaymentRefundedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        _ = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        var saga = await sagas.FindAsync(integrationEvent.OrderId, cancellationToken);
        var data = saga is null ? null : OrderFulfillmentSagaData.Parse(saga.Data);
        if (data?.RefundRequestId != integrationEvent.RefundRequestId
            || data.PaymentId != integrationEvent.PaymentId)
        {
            throw new InvalidOperationException("Payment refund acknowledgement does not match the order compensation request.");
        }
        await OrderCompensation.MarkAsync(integrationEvent.OrderId, inventory: false, payment: true, sagas, clock.GetUtcNow(), cancellationToken);
    }
}

internal sealed class PaymentRefundFailedHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock)
    : OrderEventHandler<PaymentRefundFailedIntegrationEvent>(db, outbox, "ordering.payment-refund-failed.v1")
{
    protected override async Task HandleCoreAsync(
        PaymentRefundFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        _ = await OrderRowLock.LockAsync(Db, integrationEvent.OrderId, cancellationToken);
        var saga = await sagas.FindAsync(integrationEvent.OrderId, cancellationToken);
        var data = saga is null ? null : OrderFulfillmentSagaData.Parse(saga.Data);
        if (data?.RefundRequestId != integrationEvent.RefundRequestId
            || data.PaymentId != integrationEvent.PaymentId)
        {
            throw new InvalidOperationException("Payment refund failure does not match the order compensation request.");
        }
        saga?.Fail($"Payment refund requires manual intervention: {integrationEvent.Reason}", clock.GetUtcNow());
    }
}

internal sealed class OrderSagaTimeoutHandler(
    OrderDbContext db,
    IOutboxStore outbox,
    OrderCancellationWorkflow cancellation,
    TimeProvider clock) : ISagaTimeoutHandler
{
    public string SagaType => OrderFulfillmentSagaData.SagaType;

    public async Task HandleTimeoutAsync(SagaState saga, CancellationToken cancellationToken)
    {
        var order = await OrderRowLock.LockAsync(db, saga.CorrelationId, cancellationToken);
        var now = clock.GetUtcNow();

        if (saga.State == OrderFulfillmentSagaData.AwaitingInventoryCommit
            && order.ReservationId is Guid commitReservationId)
        {
            outbox.AddIntegrationEvent(new CommitInventoryIntegrationEvent
            {
                OrderId = order.Id,
                ReservationId = commitReservationId
            });
            saga.ScheduleTimeout(now.AddMinutes(5), now);
            return;
        }

        if (saga.Status == SagaStatus.Compensating)
        {
            cancellation.RepublishOutstandingCompensations(order, OrderFulfillmentSagaData.Parse(saga.Data), saga.State);
            saga.ScheduleTimeout(now.AddMinutes(5), now);
            return;
        }

        await cancellation.CancelAsync(order, saga, "Order workflow timed out.", "system", now, cancellationToken);
    }
}

internal static class OrderRowLock
{
    public static Task<Order> LockAsync(
        OrderDbContext db,
        Guid orderId,
        CancellationToken cancellationToken) =>
        db.Orders
            .FromSqlInterpolated($"SELECT * FROM order_orders WHERE \"Id\" = {orderId} FOR UPDATE")
            .SingleAsync(cancellationToken);
}

internal static class OrderCompensation
{
    public static async Task MarkAsync(
        Guid orderId,
        bool inventory,
        bool payment,
        ISagaStore sagas,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var saga = await sagas.FindAsync(orderId, cancellationToken);
        if (saga?.Status != SagaStatus.Compensating)
            return;
        var current = OrderFulfillmentSagaData.Parse(saga.Data);
        var data = current with
        {
            InventoryCompensated = inventory || current.InventoryCompensated,
            PaymentCompensated = payment || current.PaymentCompensated
        };
        if (data.IsCompensationComplete)
        {
            saga.Compensate(now);
            return;
        }
        saga.TransitionTo(saga.State, JsonSerializer.Serialize(data), now);
    }
}
