using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.Infrastrucute.Mediator;
using MSA.NotificationService.Domain;
using MSA.NotificationService.Infrastructure;

namespace MSA.NotificationService.Application;

public sealed record ListNotificationsQuery(
    string? UserId,
    NotificationStatus? Status,
    string? TemplateCode,
    int Skip,
    int Take) : IQuery<NotificationPage>;

public sealed record NotificationListItemDto(
    Guid Id,
    string UserId,
    string TemplateCode,
    string Subject,
    string Body,
    NotificationStatus Status,
    int RetryCount,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset NextAttemptOnUtc,
    DateTimeOffset? SentOnUtc,
    string? LastError);

public sealed record NotificationPage(
    IReadOnlyList<NotificationListItemDto> Items,
    int Skip,
    int Take,
    int Total);

internal sealed class ListNotificationsHandler(NotificationDbContext db)
    : IRequestHandler<ListNotificationsQuery, NotificationPage>
{
    public async Task<NotificationPage> Handle(
        ListNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 200);
        var query = db.Notifications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var userId = request.UserId.Trim();
            query = query.Where(notification => notification.UserId == userId);
        }
        if (request.Status is { } status)
            query = query.Where(notification => notification.Status == status);
        if (!string.IsNullOrWhiteSpace(request.TemplateCode))
        {
            var templateCode = request.TemplateCode.Trim();
            query = query.Where(notification => notification.TemplateCode == templateCode);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(notification => notification.CreatedOnUtc)
            .Skip(skip)
            .Take(take)
            .Select(notification => new NotificationListItemDto(
                notification.Id,
                notification.UserId,
                notification.TemplateCode,
                notification.Subject,
                notification.Body,
                notification.Status,
                notification.RetryCount,
                notification.CreatedOnUtc,
                notification.NextAttemptOnUtc,
                notification.SentOnUtc,
                notification.LastError))
            .ToArrayAsync(cancellationToken);
        return new NotificationPage(items, skip, take, total);
    }
}

public sealed record ListNotificationTemplatesQuery(bool? Active)
    : IQuery<IReadOnlyList<NotificationTemplateDto>>;

public sealed record NotificationTemplateDto(
    Guid Id,
    string Code,
    string Subject,
    string Body,
    bool IsActive);

internal sealed class ListNotificationTemplatesHandler(NotificationDbContext db)
    : IRequestHandler<ListNotificationTemplatesQuery, IReadOnlyList<NotificationTemplateDto>>
{
    public async Task<IReadOnlyList<NotificationTemplateDto>> Handle(
        ListNotificationTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.Templates.AsNoTracking();
        if (request.Active is { } active)
            query = query.Where(template => template.IsActive == active);

        return await query
            .OrderBy(template => template.Code)
            .Select(template => new NotificationTemplateDto(
                template.Id,
                template.Code,
                template.Subject,
                template.Body,
                template.IsActive))
            .ToArrayAsync(cancellationToken);
    }
}

public sealed record SendAdminNotificationsCommand(
    IReadOnlyList<string> UserIds,
    string TemplateCode,
    string Subject,
    string Body,
    string IdempotencyKey) : ICommand<SendAdminNotificationsResult>;

public sealed record SentNotificationDto(Guid Id, string UserId);

public sealed record SendAdminNotificationsResult(
    IReadOnlyList<SentNotificationDto> Notifications,
    bool Replayed);

internal sealed class SendAdminNotificationsHandler(NotificationDbContext db)
    : IRequestHandler<SendAdminNotificationsCommand, SendAdminNotificationsResult>
{
    public async Task<SendAdminNotificationsResult> Handle(
        SendAdminNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        var userIds = request.UserIds
            .Select(userId => userId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var keys = userIds.ToDictionary(
            userId => userId,
            userId => $"admin:{request.IdempotencyKey}:{userId}",
            StringComparer.Ordinal);
        var deduplicationKeys = keys.Values.ToArray();
        var existing = await db.Notifications
            .Where(notification => deduplicationKeys.Contains(notification.DeduplicationKey))
            .Select(notification => new SentNotificationDto(notification.Id, notification.UserId))
            .ToArrayAsync(cancellationToken);
        var existingUsers = existing
            .Select(notification => notification.UserId)
            .ToHashSet(StringComparer.Ordinal);
        var created = userIds
            .Where(userId => !existingUsers.Contains(userId))
            .Select(userId => new Notification(
                Guid.NewGuid(),
                userId,
                request.TemplateCode,
                request.Subject,
                request.Body,
                keys[userId]))
            .ToArray();
        db.Notifications.AddRange(created);

        return new SendAdminNotificationsResult(
            existing.Concat(created.Select(notification =>
                new SentNotificationDto(notification.Id, notification.UserId))).ToArray(),
            created.Length == 0);
    }
}

public sealed record RetryNotificationCommand(Guid NotificationId)
    : ICommand<RetryNotificationResult>;

public sealed record RetryNotificationResult(bool Found, bool Changed);

internal sealed class RetryNotificationHandler(
    NotificationDbContext db,
    TimeProvider clock) : IRequestHandler<RetryNotificationCommand, RetryNotificationResult>
{
    public async Task<RetryNotificationResult> Handle(
        RetryNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(
            value => value.Id == request.NotificationId,
            cancellationToken);
        return notification is null
            ? new RetryNotificationResult(false, false)
            : new RetryNotificationResult(true, notification.Retry(clock.GetUtcNow()));
    }
}
