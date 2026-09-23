using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MSA.Repositories.Persistence;

public sealed class MessagingRetentionOptions
{
    /// <summary>已发布的 Outbox 记录保留多久，用于排查与审计。</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Inbox 去重记录保留多久。必须显著长于消息可能的最大重投时间（重试 + 死信重放），
    /// 否则过早清理会让重复消息被当成新消息再执行一次。
    /// </summary>
    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromHours(1);

    public int BatchSize { get; set; } = 2_000;
}

/// <summary>
/// 定期清理已发布的 Outbox 与过期的 Inbox 记录：没有它，这两张表只会无限增长，
/// 最终拖慢抢占查询这条关键路径。
/// </summary>
internal sealed class MessagingRetentionService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<MessagingRetentionOptions> options,
    ILogger<MessagingRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ScanInterval, clock);
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Messaging retention pass failed; retrying on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IMessagingOperations>();
        var now = clock.GetUtcNow();
        var (outbox, inbox) = await operations.PurgeProcessedMessagesAsync(
            now - options.Value.OutboxRetention,
            now - options.Value.InboxRetention,
            options.Value.BatchSize,
            cancellationToken);
        if (outbox + inbox > 0)
            logger.LogInformation("Purged {Outbox} published outbox rows and {Inbox} inbox rows.", outbox, inbox);
    }
}

public static class MessagingRetentionServiceCollectionExtensions
{
    /// <summary>注册 Outbox/Inbox 的保留期清理，并暴露 <see cref="IMessagingOperations"/> 供运维接口使用。</summary>
    public static IServiceCollection AddMessagingOperations<TContext>(
        this IServiceCollection services,
        Action<MessagingRetentionOptions>? configure = null)
        where TContext : MessagingDbContext
    {
        services.AddScoped<IMessagingOperations>(provider => provider.GetRequiredService<TContext>());
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingRetentionOptions>().Configure(options => configure?.Invoke(options));
        services.AddHostedService<MessagingRetentionService>();
        return services;
    }
}
