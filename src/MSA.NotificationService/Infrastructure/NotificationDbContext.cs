using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.NotificationService.Domain;
using MSA.Repositories.Persistence;

namespace MSA.NotificationService.Infrastructure;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options, IMediator mediator)
    : MessagingDbContext(options, mediator, "notification")
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();
    public DbSet<UserNotificationPreference> Preferences => Set<UserNotificationPreference>();

    internal async Task<Notification?> LockNotificationAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!HasActiveTransaction)
            throw new InvalidOperationException("Notification retry requires an active transaction.");

        // 串行化管理员重试，避免两个请求用同一旧版本更新通知状态。
        return (await Notifications
            .FromSqlInterpolated($"SELECT * FROM notification_notifications WHERE \"Id\" = {id} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
