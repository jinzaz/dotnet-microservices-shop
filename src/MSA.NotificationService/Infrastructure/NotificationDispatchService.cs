using Microsoft.EntityFrameworkCore;

namespace MSA.NotificationService.Infrastructure;

public interface INotificationSender
{
    Task SendAsync(string userId, string subject, string body, CancellationToken cancellationToken);
}

internal sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(string userId, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("Notification delivered to {UserId}: {Subject}", userId, subject);
        return Task.CompletedTask;
    }
}

internal sealed class NotificationDispatchService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private readonly Guid _workerId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            await DispatchBatchAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Notifications
            .Where(x => x.NextAttemptOnUtc <= now &&
                (x.Status == Domain.NotificationStatus.Pending ||
                 x.Status == Domain.NotificationStatus.Sending && x.LockedUntilUtc <= now))
            .OrderBy(x => x.NextAttemptOnUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var claimed = rows.Where(x => x.TryClaim(_workerId, now, LeaseDuration)).ToArray();
        if (claimed.Length == 0)
            return;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 其他实例已抢到租约；本轮直接退出，下次轮询会重新加载最新状态。
            return;
        }

        foreach (var row in claimed)
        {
            try
            {
                await sender.SendAsync(row.UserId, row.Subject, row.Body, cancellationToken);
                row.Sent(_workerId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification {NotificationId} failed", row.Id);
                row.Failed(_workerId, exception);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
