using Microsoft.EntityFrameworkCore;
using MSA.Contracts;
using MSA.EventBusOutbox;
using System.Text.Json;

namespace MSA.PaymentService.Infrastructure;

internal sealed class PaymentExpiryService(IServiceScopeFactory scopeFactory, ILogger<PaymentExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            await ExpireBatchAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var payments = await db.Payments
                .Where(x => x.Status == Domain.PaymentStatus.Pending && x.ExpiresOnUtc <= DateTimeOffset.UtcNow)
                .OrderBy(x => x.ExpiresOnUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var payment in payments)
            {
                const string reason = "Payment expired.";
                if (!payment.Fail(reason))
                    continue;
                var message = new PaymentFailedIntegrationEvent { OrderId = payment.OrderId, PaymentId = payment.Id, Reason = reason };
                outbox.Add(OutboxMessage.Create(message.GetType().AssemblyQualifiedName!, JsonSerializer.Serialize(message, message.GetType())));
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await db.RollbackTransactionAsync(cancellationToken);
            logger.LogDebug(exception, "Another payment worker expired the same batch.");
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
