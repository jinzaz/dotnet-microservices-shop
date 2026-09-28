using Microsoft.EntityFrameworkCore;
using MSA.Persistence.Postgres;
using Microsoft.Extensions.Options;
using MSA.Contracts;
using MSA.EventBusOutbox;
using MSA.PaymentService.Domain;
using System.Text.Json;

namespace MSA.PaymentService.Infrastructure;

internal sealed class PaymentExpiryService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<PaymentProcessingOptions> configured,
    ILogger<PaymentExpiryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(configured.Value.ExpiryPollingInterval, clock);
        do
        {
            try
            {
                await ExpireBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Payment expiry polling failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var now = clock.GetUtcNow();
        var batchSize = configured.Value.ExpiryBatchSize;

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            var payments = await db.Payments
                .Where(payment => payment.Status == PaymentStatus.Pending && payment.ExpiresOnUtc <= now)
                .OrderBy(payment => payment.ExpiresOnUtc)
                .ThenBy(payment => payment.Id)
                .Take(batchSize)
                .ForUpdateSkipLocked()
                .AsTracking()
                .ToListAsync(cancellationToken);

            foreach (var payment in payments)
            {
                const string reason = "Payment expired.";
                var previousStatus = payment.Status;
                if (!payment.Expire(now, reason))
                    continue;

                db.PaymentStatusHistory.Add(PaymentStatusHistory.Record(
                    payment.Id,
                    previousStatus,
                    payment.Status,
                    "PaymentExpired",
                    now,
                    reason));
                var integrationEvent = new PaymentFailedIntegrationEvent
                {
                    OrderId = payment.OrderId,
                    PaymentId = payment.Id,
                    Reason = reason
                };
                outbox.Add(OutboxMessage.Create(
                    integrationEvent.GetType().AssemblyQualifiedName!,
                    JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
                    now));
            }

            await db.CommitTransactionAsync(transaction, cancellationToken);
        }
        catch
        {
            await db.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
