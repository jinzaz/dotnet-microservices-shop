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
          // 一轮失败（例如数据库重启）不能让发布器退出：BackgroundService 的未处理异常默认会停掉整个进程。
        do
        {
            try
            {
                // 取满一批说明还有积压，立刻取下一批；取不满再等下一个周期。发布失败的消息会被推迟到
                // NextAttemptOnUtc 之后，不会在本轮被重新领取，所以不会空转。
                while (await ProcessBatchAsync(stoppingToken) == options.Value.BatchSize) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox publishing round failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>(); var bus = scope.ServiceProvider.GetRequiredService<IEventBus>(); var lockId = Guid.NewGuid();
        IReadOnlyList<OutboxMessage> messages;
        try { messages = await store.ClaimPendingAsync(lockId, options.Value.BatchSize, options.Value.MaxRetries, clock.GetUtcNow(), options.Value.LockDuration, cancellationToken); }
        catch (DbUpdateConcurrencyException) { logger.LogDebug("Another worker claimed this outbox batch."); return 0; }
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
                message.MarkFailed(lockId, exception, clock.GetUtcNow().AddSeconds(seconds + jitter), options.Value.MaxRetries);
                if (message.Status == OutboxStatus.Failed)
                {
                    // 重试用尽后不会再自动发布：必须让它在日志与告警里显眼，再由运维接口重投。
                    logger.LogCritical(exception,
                        "Outbox message {MessageId} ({EventType}) gave up after {RetryCount} attempts and needs a manual requeue.",
                        message.Id, message.EventType, message.RetryCount);
                }
                else
                {
                    logger.LogError(exception, "Failed to publish outbox message {MessageId}", message.Id);
                }
            }
            await store.SaveChangesAsync(cancellationToken);
        }
        return messages.Count;
    }
}
