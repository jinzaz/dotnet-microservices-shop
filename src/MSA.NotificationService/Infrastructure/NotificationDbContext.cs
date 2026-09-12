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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
