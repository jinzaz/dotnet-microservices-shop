using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSA.EventBus.Abstractions;
using System.Text.Json;
namespace MSA.EventBusOutbox;
internal sealed class OutboxProcessorService(IServiceScopeFactory scopes, TimeProvider clock, IOptions<OutboxProcessorOptions> options, ILogger<OutboxProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, clock);
        do { await ProcessBatchAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>(); var bus = scope.ServiceProvider.GetRequiredService<IEventBus>(); var lockId = Guid.NewGuid();
        IReadOnlyList<OutboxMessage> messages;
        try { messages = await store.ClaimPendingAsync(lockId, options.Value.BatchSize, options.Value.MaxRetries, clock.GetUtcNow(), options.Value.LockDuration, cancellationToken); }
        catch (DbUpdateConcurrencyException) { logger.LogDebug("Another worker claimed this outbox batch."); return; }
        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.EventType) ?? throw new InvalidOperationException($"Unknown event type '{message.EventType}'.");
                var value = JsonSerializer.Deserialize(message.Payload, type) as IntegrationEvent ?? throw new JsonException("Invalid integration event payload.");
                await bus.PublishAsync(value, cancellationToken); message.MarkPublished(lockId, clock.GetUtcNow());
            }
            catch (Exception exception)
            {
                var seconds = Math.Min(Math.Pow(2, message.RetryCount + 1), options.Value.MaximumRetryDelay.TotalSeconds); var jitter = Random.Shared.NextDouble() * Math.Min(5, seconds * .2);
                message.MarkFailed(lockId, exception, clock.GetUtcNow().AddSeconds(seconds + jitter), options.Value.MaxRetries); logger.LogError(exception, "Failed to publish outbox message {MessageId}", message.Id);
            }
            await store.SaveChangesAsync(cancellationToken);
        }
    }
}
