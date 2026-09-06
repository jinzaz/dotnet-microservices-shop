using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace MSA.Saga;

internal sealed class SagaTimeoutService(IServiceScopeFactory scopes, TimeProvider clock, IOptions<SagaTimeoutOptions> options, ILogger<SagaTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, clock);
        do { await ProcessAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var lockId = Guid.NewGuid();
        Guid[] correlationIds;
        using (var claimScope = scopes.CreateScope())
        {
            var claimStore = claimScope.ServiceProvider.GetRequiredService<ISagaStore>();
            try
            {
                correlationIds = (await claimStore.ClaimDueTimeoutsAsync(
                        lockId,
                        options.Value.BatchSize,
                        clock.GetUtcNow(),
                        options.Value.LockDuration,
                        cancellationToken))
                    .Select(saga => saga.CorrelationId)
                    .ToArray();
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogDebug("Another worker claimed this saga timeout batch.");
                return;
            }
        }

        // 每个超时任务使用独立 DbContext，避免某次回滚留下的跟踪状态泄漏到后续 Saga 提交。
        foreach (var correlationId in correlationIds)
        {
            using var handlingScope = scopes.CreateScope();
            var store = handlingScope.ServiceProvider.GetRequiredService<ISagaStore>();
            try
            {
                var saga = await store.FindAsync(correlationId, cancellationToken);
                if (saga?.TimeoutLockId != lockId)
                    continue;
                var handlers = handlingScope.ServiceProvider
                    .GetServices<ISagaTimeoutHandler>()
                    .ToDictionary(handler => handler.SagaType, StringComparer.Ordinal);
                await using var transaction = await store.BeginTransactionAsync(cancellationToken);
                if (!handlers.TryGetValue(saga.SagaType, out var handler)) throw new InvalidOperationException($"No timeout handler is registered for saga '{saga.SagaType}'.");
                await handler.HandleTimeoutAsync(saga, cancellationToken);
                if (saga.TimeoutLockId == lockId) saga.ClearTimeout(lockId, clock.GetUtcNow());
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) { logger.LogError(exception, "Saga timeout handling failed for {CorrelationId}", correlationId); }
        }
    }
}
