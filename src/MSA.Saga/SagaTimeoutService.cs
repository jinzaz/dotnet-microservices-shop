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
        using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<ISagaStore>(); var lockId = Guid.NewGuid(); IReadOnlyList<SagaState> sagas;
        try { sagas = await store.ClaimDueTimeoutsAsync(lockId, options.Value.BatchSize, clock.GetUtcNow(), options.Value.LockDuration, cancellationToken); }
        catch (DbUpdateConcurrencyException) { logger.LogDebug("Another worker claimed this saga timeout batch."); return; }
        var handlers = scope.ServiceProvider.GetServices<ISagaTimeoutHandler>().ToDictionary(x => x.SagaType, StringComparer.Ordinal);
        foreach (var saga in sagas)
        {
            try
            {
                if (!handlers.TryGetValue(saga.SagaType, out var handler)) throw new InvalidOperationException($"No timeout handler is registered for saga '{saga.SagaType}'.");
                await handler.HandleTimeoutAsync(saga, cancellationToken);
                if (saga.TimeoutLockId == lockId) saga.ClearTimeout(lockId, clock.GetUtcNow());
                await store.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) { logger.LogError(exception, "Saga timeout handling failed for {CorrelationId}", saga.CorrelationId); }
        }
    }
}
