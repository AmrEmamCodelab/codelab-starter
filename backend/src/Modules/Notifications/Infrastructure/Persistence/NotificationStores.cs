using Microsoft.EntityFrameworkCore;
using Starter.Application.Notifications;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence;

/// <summary>The <see cref="INotificationStore"/> over <see cref="NotificationsDbContext"/>.</summary>
public sealed class EfNotificationStore(NotificationsDbContext context) : INotificationStore
{
    /// <inheritdoc />
    public void Add(Notification notification) => context.Notifications.Add(notification);

    /// <inheritdoc />
    public Task<Notification?> FindAsync(long id, CancellationToken cancellationToken) =>
        context.Notifications.FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Notification?> FindInAppAsync(FeedOwner owner, long id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return InApp(owner).FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Notification>> ListInAppAsync(FeedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var items = InApp(query.Owner).AsNoTracking();
        if (query.BeforeId is { } beforeId)
        {
            items = items.Where(notification => notification.Id < beforeId);
        }

        return await items.OrderByDescending(notification => notification.Id).Take(query.Take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(FeedOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return InApp(owner).CountAsync(notification => notification.ReadAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private IQueryable<Notification> InApp(FeedOwner owner) =>
        context.Notifications.Where(notification =>
            notification.UserId == owner.UserId
            && (notification.Channels & NotificationChannels.InApp) == NotificationChannels.InApp);
}

/// <summary>The <see cref="ITemplateStore"/> over <see cref="NotificationsDbContext"/> (no tracking: the texts are read-only).</summary>
public sealed class EfTemplateStore(NotificationsDbContext context) : ITemplateStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(string key, CancellationToken cancellationToken) =>
        await context.Templates.AsNoTracking().Where(template => template.Key == key).ToListAsync(cancellationToken);
}
