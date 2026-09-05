using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.OrderService.Domain;
using MSA.OrderService.Infrastructure;
using MSA.Saga;
using System.Text.Json;

namespace MSA.OrderService.Application.CreateOrder;

internal sealed class CreateOrderCommandHandler(
    OrderDbContext db,
    IOrderRepository orders,
    IOrderSubmissionLock submissionLock,
    IOutboxStore outbox,
    ISagaStore sagas,
    TimeProvider clock) : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = request.IdempotencyKey.Trim();
        await submissionLock.AcquireAsync(
            [OrderSubmissionLockKeys.Idempotency(request.UserId, idempotencyKey)],
            cancellationToken);

        var existing = await db.Orders
            .AsNoTracking()
            .Where(order => order.UserId == request.UserId && order.IdempotencyKey == idempotencyKey)
            .Select(order => (Guid?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing.HasValue)
            return existing.Value;

        var skus = request.Items.Select(item => item.Sku).Distinct(StringComparer.Ordinal).ToArray();
        var products = await db.ProductProjections
            .AsNoTracking()
            .Where(product => skus.Contains(product.Sku) && product.IsActive)
            .ToDictionaryAsync(product => product.Sku, StringComparer.Ordinal, cancellationToken);
        if (products.Count != skus.Length)
            throw new InvalidOperationException("One or more products are unavailable.");

        var currencies = products.Values.Select(product => product.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length != 1)
            throw new InvalidOperationException("Mixed currencies are not supported.");

        var now = clock.GetUtcNow();
        var order = new Order(
            Guid.NewGuid(),
            request.UserId,
            idempotencyKey,
            request.Items.Select(item =>
                new OrderLine(item.Sku, products[item.Sku].Name, item.Quantity, products[item.Sku].Price)),
            currencies[0],
            createdOnUtc: now);
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
            PaymentMethod = request.PaymentMethod
        };
        outbox.Add(OutboxMessage.Create(
            integrationEvent.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())));

        var saga = SagaState.Start(
            order.Id,
            "OrderFulfillment",
            "AwaitingInventory",
            JsonSerializer.Serialize(new { order.UserId, request.PaymentMethod }),
            now);
        saga.ScheduleTimeout(now.AddMinutes(20), now);
        sagas.Add(saga);
        return order.Id;
    }
}
