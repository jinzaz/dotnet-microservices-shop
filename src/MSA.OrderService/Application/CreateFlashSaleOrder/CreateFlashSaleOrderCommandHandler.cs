using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CreateFlashSaleOrder;

internal sealed class CreateFlashSaleOrderCommandHandler(
    OrderDbContext db,
    IOrderRepository orders,
    IOrderSubmissionLock submissionLock,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock) : IRequestHandler<CreateFlashSaleOrderCommand, Guid>
{
    public async Task<Guid> Handle(
        CreateFlashSaleOrderCommand request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        await submissionLock.AcquireAsync(
            [
                OrderSubmissionLockKeys.Idempotency(request.UserId, idempotencyKey),
                OrderSubmissionLockKeys.FlashSaleBuyer(request.CampaignId, request.UserId)
            ],
            cancellationToken);

        var idempotentOrder = await db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == request.UserId && order.IdempotencyKey == idempotencyKey)
            .Select(order => new { order.Id, order.FlashSaleCampaignId })
            .SingleOrDefaultAsync(cancellationToken);
        if (idempotentOrder is not null)
        {
            if (idempotentOrder.FlashSaleCampaignId != request.CampaignId)
                throw new InvalidOperationException("The idempotency key is already bound to another order request.");
            return idempotentOrder.Id;
        }

        var previousCampaignOrderId = await db.Orders
            .AsNoTracking()
            .Where(order =>
                order.UserId == request.UserId
                && order.FlashSaleCampaignId == request.CampaignId)
            .Select(order => (Guid?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (previousCampaignOrderId.HasValue)
            return previousCampaignOrderId.Value;

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
        var order = new Order(
            Guid.NewGuid(),
            request.UserId,
            idempotencyKey,
            [new OrderLine(product.Sku, product.Name, request.Quantity, product.Price)],
            product.Currency,
            request.CampaignId,
            now);
        orders.Add(order);

        var integrationEvent = new OrderSubmittedIntegrationEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Items = order.Lines
                .Select(line => new OrderItemContract(line.Sku, line.Quantity, line.UnitPrice))
                .ToArray(),
            Amount = order.Amount,
            Currency = order.Currency,
            PaymentMethod = request.PaymentMethod,
            FlashSaleCampaignId = request.CampaignId
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

        var saga = SagaState.Start(
            order.Id,
            "OrderFulfillment",
            "AwaitingInventory",
            JsonSerializer.Serialize(new { order.UserId, request.PaymentMethod, request.CampaignId }),
            now);
        saga.ScheduleTimeout(now.AddMinutes(10), now);
        sagas.Add(saga);

        return order.Id;
    }
}
