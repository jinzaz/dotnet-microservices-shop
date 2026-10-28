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
        // 一批内并发发布、各自等 broker 确认，最后统一保存一次。每条消息只改自己的实体，不碰 DbContext，
        // 所以并发是安全的；只有确认成功的才标记已发布，失败的各自按退避重试，保持至少投递一次。
        // 统一保存失败（例如租约过期被别的实例接管）时整批都不落库，租约到期后会被重发，消费端靠 Inbox 去重。
        var parallelism = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.PublishConcurrency), CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(messages, parallelism, async (message, token) =>
        {
            try
            {
                var type = Type.GetType(message.EventType) ?? throw new InvalidOperationException($"Unknown event type '{message.EventType}'.");
                var value = JsonSerializer.Deserialize(message.Payload, type) as IntegrationEvent ?? throw new JsonException("Invalid integration event payload.");
                await bus.PublishAsync(value, token); message.MarkPublished(lockId, clock.GetUtcNow());
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
        });
        await store.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}
